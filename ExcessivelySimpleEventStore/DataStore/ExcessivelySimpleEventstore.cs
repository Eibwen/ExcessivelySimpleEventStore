using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using TKey = System.String;

namespace ExcessivelySimpleEventStore.DataStore
{
    /// <summary>
    /// A super minimal event store implementation from my (hopefully) understanding the concepts but never looking at any implementations
    /// </summary>
    public class ExcessivelySimpleEventStore<TValue>
    {
        private int FlushFrequencySeconds = 5;

        private readonly Dictionary<TKey, TValue> _datastore;
        readonly Func<TValue, TKey> _getKey;
        readonly string _fileStorageLocation;

        readonly object _controller;
        readonly Dictionary<string, (bool isCommand, MethodInfo method)> _controllerEvents;

        readonly Queue<string> _writeQueue = new Queue<string>();
        // ReSharper disable once NotAccessedField.Local
        private readonly Task _writerTask;

        public ExcessivelySimpleEventStore(object controller, Func<TValue, TKey> getKey, string fileStorageLocation)
        {
            _controller = controller;
            _getKey = getKey;
            _fileStorageLocation = fileStorageLocation;

            _controllerEvents = BuildEvents(controller);

            if (File.Exists(fileStorageLocation))
            {
                //Format is "<event>\t<params>"
                //  <params> is "{
                var events = File.ReadAllLines(fileStorageLocation)
                                .Where(x => !x.StartsWith("#"))
                                .Select(x => x.Split('\t'))
                                .Select(x => new
                                {
                                    Event = x[0],
                                    Params = x[1]
                                });

                foreach (var e in events)
                {
                    ExecuteEvent_Internal(e.Event, e.Params, true);
                }
            }
            else
            {
                _datastore = new Dictionary<TKey, TValue>();
            }

            //Startup writer Task
            _writerTask = DoWorkAsyncInfiniteLoop(WriteQueueToDisk);
        }
        private Dictionary<string, (bool isCommand, MethodInfo method)> BuildEvents(object controller)
        {
            var methods = controller.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                            .Where(m => !m.IsSpecialName)
                            .Where(m => m.DeclaringType != typeof(object));

            var invalidReturnTypes = methods.Where(x => x.ReturnType != typeof(void) && x.ReturnType != typeof(TValue));
            if (invalidReturnTypes.Any())
            {
                throw new Exception($"Controller has methods defined which have a return value which is invalid.  Commands must either return {GetFriendlyName(typeof(TValue))} or void.  Offending method(s): {string.Join(", ", invalidReturnTypes.Select(x => x.Name))}");
            }
            return methods.ToDictionary(x => x.Name, v => (IsValidControllerCommand(v), v), StringComparer.InvariantCultureIgnoreCase);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <remarks>
        /// TODO consider throwing an exception if ANY public methods do not match this
        ///      But that would require an attribute for like [NotCommand] to still allow public methods
        ///      Bonus there is it is much more fail-fast
        /// </remarks>
        /// <param name="method"></param>
        /// <returns></returns>
        public bool IsValidControllerCommand(MethodInfo method)
        {
            var parameters = method.GetParameters();
            if (parameters.Length != 2)
            {
                return false;
            }
            var databaseAsFirstParameter = parameters[0].ParameterType;
            if (databaseAsFirstParameter.GetGenericTypeDefinition() != typeof(ExcessivelySimpleEventStore<>))
            {
                return false;
            }
            if (!databaseAsFirstParameter.GenericTypeArguments.Any()
                || databaseAsFirstParameter.GenericTypeArguments[0] != typeof(TValue))
            {
                return false;
            }

            return true;
        }

        // Read-only
        public TValue Get(TKey key)
        {
            if (_datastore.TryGetValue(key, out var value))
            {
                return value;
            }
            return default(TValue);
        }

        /// <summary>
        /// Avoid using this for common tasks.  Datamigration is fine, but this is definitely not optimized
        /// </summary>
        public IEnumerable<TValue> Query(Func<KeyValuePair<TKey, TValue>, bool> selection)
        {
            return _datastore.Where(selection).Select(x => x.Value);
        }


        // State change functions
        public void AddOrUpdate(TValue value)
        {
            LogVerboseEvent("+", value);
            AddOrUpdate_Internal(value);
        }

        //private void ExecuteEvent(string command, object payload)
        //{
        //    LogEvent(command, payload);
        //    ExecuteEvent_Internal(command, payload, false);
        //}

        ///Use for commands with a return value
        private void AddOrUpdate_Internal(TValue value)
        {
            //TODO should  && !Object.ReferenceEquals(existingValue, newValue), and check Id property
            _datastore[_getKey(value)] = value;
        }

        private void ExecuteEvent_Internal(string command, object payload, bool isInitializing)
        {
            if (_controllerEvents.TryGetValue(command, out var cmd))
            {
                if (!cmd.isCommand)
                {
                    throw new Exception($"Received command that exists but doesn't have a valid signature: {command}({GetFriendlyName(this.GetType())} datastore, ...)");
                }

                if (isInitializing)
                {
                    var paramType = cmd.method.GetParameters()[0].ParameterType;
                    payload = JsonConvert.DeserializeObject((string)payload, paramType);
                }

                var returnValue = cmd.method.Invoke(_controller, new[] { this, payload });
                if (returnValue != null)
                {
                    //Actually this might cause all kinds of reference problems if used improperly... should I protect against that?
                    //  In the sense that this datastore should load/save properly and be consistent...
                    //  ACTUALLY due to that, I think it would be consistent when reloading
                    //  Just modifications might be weird
                    AddOrUpdate_Internal((TValue)returnValue);
                }
            }
            else
            {
                throw new Exception("Unknown command (needs to be implemented in the controller): " + command + " -- which must only have ONE parameter of any object type");
            }
        }
        private static string GetFriendlyName(Type type)
        {
            if (type.IsGenericType)
                return $"{type.Name.Split('`')[0]}<{string.Join(", ", type.GetGenericArguments().Select(GetFriendlyName))}>";
            else
                return type.Name;
        }


        // Write-only
        private void LogEvent(string command, object payload)
        {
            _writeQueue.Enqueue($"{command}\t{JsonConvert.SerializeObject(payload)}");
        }
        private void LogVerboseEvent(string command, object payload)
        {
            _writeQueue.Enqueue($"# {command}\t{JsonConvert.SerializeObject(payload)}");
        }

        private async Task WriteQueueToDisk()
        {
            if (_writeQueue.Count > 0)
            {
                using (FileStream fs = new FileStream(_fileStorageLocation,
                    FileMode.Append, FileAccess.Write, FileShare.None,
                    bufferSize: 4096, useAsync: false))
                using (StreamWriter sw = new StreamWriter(fs, Encoding.UTF8, 4096))
                {
                    //await sourceStream.WriteAsync(encodedText, 0, encodedText.Length);
                    while (_writeQueue.Count > 0)
                    {
                        var line = _writeQueue.Dequeue();

                        await sw.WriteLineAsync(line);
                    }
                }
            }
        }

        private async Task DoWorkAsyncInfiniteLoop(Func<Task> work)
        {
            while (true)
            {
                await work();

                await Task.Delay(TimeSpan.FromSeconds(FlushFrequencySeconds));
            }
        }
    }
}