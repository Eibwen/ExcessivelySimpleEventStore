using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using TKey = System.String;

namespace ExcessivelySimpleEventStore.DataStore
{
    public interface IEventStoreAction<TValue>
    {
        TValue Get(TKey key);

        /// <summary>
        /// Avoid using this for common tasks.  Data migration is fine, but this is definitely not optimized
        /// </summary>
        IEnumerable<TValue> Query(Func<KeyValuePair<TKey, TValue>, bool> selection);

        void AddOrUpdate(TValue value);
        void ExecuteEvent(string command, object payload);
        void ExecuteEvent<T>(T payload);
    }

    /// <summary>
    /// A super minimal event store implementation from my (hopefully) understanding the concepts but never looking at any implementations
    /// </summary>
    public class EventStore<TController, TValue> : IEventStoreAction<TValue>, IDisposable
    {
        private int FlushFrequencySeconds = 5;

        private readonly Dictionary<TKey, TValue> _datastore;
        readonly Func<TValue, TKey> _getKey;
        private readonly IFileSystem _fileSystem;
        readonly string _fileStorageLocation;

        readonly object _controller;
        private readonly Dictionary<string, CommandInfo> _controllerEvents;
        private readonly Dictionary<Type, CommandInfo> _controllerEventLookupByPayload;

        readonly Queue<string> _writeQueue = new Queue<string>();
        // ReSharper disable once NotAccessedField.Local
        private readonly Task _writerTask;

        public EventStore(TController controller, Func<TValue, TKey> getKey, IFileSystem fileSystem, string fileStorageLocation)
        {
            _controller = controller;
            _getKey = getKey;
            _fileSystem = fileSystem;
            _fileStorageLocation = fileStorageLocation;

            BuildLookups(controller, out _controllerEvents, out _controllerEventLookupByPayload);

            _datastore = new Dictionary<TKey, TValue>();

            if (_fileSystem.File.Exists(fileStorageLocation))
            {
                //Format is "<event>\t<params>"
                //  <params> is "{
                var events = _fileSystem.File.ReadAllLines(fileStorageLocation)
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

            //Startup writer Task
            _writerTask = DoWorkAsyncInfiniteLoop(WriteQueueToDisk);
        }

        private void BuildLookups(TController controller,
                                  out Dictionary<string, CommandInfo> controllerEvents,
                                  out Dictionary<Type, CommandInfo> payloadLookup)
        {
            var commands = BuildCommands(controller);
            controllerEvents = commands.ToDictionary(k => k.CommandName, StringComparer.InvariantCultureIgnoreCase);
            payloadLookup = commands
                            .GroupBy(x => GetEventPayloadType(x.Method))
                            .Where(x => x.Count() == 1)
                            .ToDictionary(x => x.Key, x => x.Single());
        }
        private IEnumerable<CommandInfo> BuildCommands(object controller)
        {
            var methods = controller.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                            .Where(m => !m.IsSpecialName)
                            .Where(m => m.DeclaringType != typeof(object))
                                    .ToList();

            var invalidReturnTypes = methods.Where(x => x.ReturnType != typeof(void) && x.ReturnType != typeof(TValue));
            if (invalidReturnTypes.Any())
            {
                throw new Exception($"Controller has methods defined which have a return value which is invalid.  Commands must either return {GetFriendlyName(typeof(TValue))} or void.  Offending method(s): {string.Join(", ", invalidReturnTypes.Select(x => x.Name))}");
            }
            return methods.Select(v => new CommandInfo(v.Name, IsValidControllerCommand(v), v));
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
        private bool IsValidControllerCommand(MethodInfo method)
        {
            var parameters = method.GetParameters();
            if (parameters.Length != 2)
            {
                return false;
            }
            var databaseAsFirstParameter = parameters[0].ParameterType;
            if (databaseAsFirstParameter.GetGenericTypeDefinition() != typeof(IEventStoreAction<>))
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

        private class CommandInfo
        {
            public CommandInfo(string commandName, bool isCommand, MethodInfo method)
            {
                CommandName = commandName;
                IsCommand = isCommand;
                Method = method;
            }

            public string CommandName { get; }
            public bool IsCommand { get; }
            public MethodInfo Method { get; }
        }

        // Read-only
        TValue IEventStoreAction<TValue>.Get(TKey key)
        {
            if (_datastore.TryGetValue(key, out var value))
            {
                return value;
            }
            return default(TValue);
        }

        /// <summary>
        /// Avoid using this for common tasks.  Data migration is fine, but this is definitely not optimized
        /// </summary>
        IEnumerable<TValue> IEventStoreAction<TValue>.Query(Func<KeyValuePair<TKey, TValue>, bool> selection)
        {
            return _datastore.Where(selection).Select(x => x.Value);
        }


        // State change functions
        void IEventStoreAction<TValue>.AddOrUpdate(TValue value)
        {
            LogVerboseEvent("+", value);
            AddOrUpdate_Internal(value);
        }

        void IEventStoreAction<TValue>.ExecuteEvent(string command, object payload)
        {
            LogEvent(command, payload);
            ExecuteEvent_Internal(command, payload, false);
        }

        void IEventStoreAction<TValue>.ExecuteEvent<T>(T payload)
        {
            if (_controllerEventLookupByPayload.TryGetValue(typeof(T), out var cmd))
            {
                LogEvent(cmd.CommandName, payload);
                ExecuteCommand_Internal(cmd, payload, false);
            }
            else
            {
                throw new Exception("Unknown event type (needs to have a SINGLE command in the controller): " + typeof(T).Name);
            }
        }

        ///Use for commands with a return value
        private void AddOrUpdate_Internal(TValue value)
        {
            //TODO should  && !Object.ReferenceEquals(existingValue, newValue), and check Id property
            _datastore[_getKey(value)] = value;
        }

        private Type GetEventPayloadType(MethodInfo method)
        {
            return method.GetParameters()[1].ParameterType;
        }

        private void ExecuteEvent_Internal(string command, object payload, bool isInitializing)
        {
            if (_controllerEvents.TryGetValue(command, out var cmd))
            {
                ExecuteCommand_Internal(cmd, payload, isInitializing);
            }
            else
            {
                throw new Exception("Unknown command (needs to be implemented in the controller): " + command + " -- which must only have ONE parameter of any object type");
            }
        }

        private void ExecuteCommand_Internal(CommandInfo cmd, object payload, bool isInitializing)
        {
            if (!cmd.IsCommand)
            {
                //TODO update this message
                throw new Exception($"Received command that exists but doesn't have a valid signature: {cmd.CommandName}({GetFriendlyName(this.GetType())} datastore, ...)");
            }

            if (isInitializing)
            {
                var paramType = GetEventPayloadType(cmd.Method);
                payload = JsonConvert.DeserializeObject((string) payload, paramType);
            }

            var returnValue = cmd.Method.Invoke(_controller, new[] {this, payload});
            if (returnValue != null)
            {
                //Actually this might cause all kinds of reference problems if used improperly... should I protect against that?
                //  In the sense that this datastore should load/save properly and be consistent...
                //  ACTUALLY due to that, I think it would be consistent when reloading
                //  Just modifications might be weird
                AddOrUpdate_Internal((TValue) returnValue);
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

        internal async Task WriteQueueToDisk()
        {
            if (_writeQueue.Count > 0)
            {
                using (var fs = _fileSystem.FileStream.Create(_fileStorageLocation,
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

        private static readonly SemaphoreSlim DoWorkSemaphore = new SemaphoreSlim(1, 1);
        private async Task DoWorkAsyncInfiniteLoop(Func<Task> work)
        {
            while (true)
            {
                await DoWorkSemaphore.WaitAsync();
                try
                {
                    await work();

                    await Task.Delay(TimeSpan.FromSeconds(FlushFrequencySeconds));
                }
                finally
                {
                    DoWorkSemaphore.Release();
                }
            }
        }

        public void Dispose()
        {
            _writerTask?.Dispose();
        }
    }
}