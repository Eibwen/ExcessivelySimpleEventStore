using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Threading;
using ExcessivelySimpleEventStore.DataStore;
using FluentAssertions;
using NUnit.Framework;

namespace ExcessivelySimpleEventStoreTests.DataStore
{
    [TestFixture]
    public class EventStoreTests
    {
        [Test]
        public void When_insert_then_retrieve()
        {
            //Arrange
            var controller = new TestController();
            var dataStoreFile = Path.Combine("fakePath", "TestDataStore.db");

            var fileSystem = GetMockFileSystem(dataStoreFile);

            var dataStore = new EventStore<TestController, TestDataType>(controller, x => x.Id.ToString(), fileSystem, dataStoreFile);

            var insertData = new TestDataType
            {
                Id = 321,
                MyData = new List<string>
                {
                    "hello",
                    "bye",
                    "three"
                }
            };


            //Act
            dataStore.AddOrUpdate(insertData);
            var loadedData = dataStore.Get(insertData.Id.ToString());

            //Assert
            loadedData.Should().BeEquivalentTo(insertData);
        }

        [Test]
        public void When_insert_then_load_from_disk_and_retrieve()
        {
            //Arrange
            var controller = new TestController();
            var dataStoreFile = Path.Combine(@"f:\fakePath", "TestDataStore.db");

            var fileSystem = GetMockFileSystem(dataStoreFile);

            var dataStore = new EventStore<TestController, TestDataType>(controller, x => x.Id.ToString(), fileSystem, dataStoreFile);

            var insertData = new TestDataType
            {
                Id = 321,
                MyData = new List<string>
                {
                    "hello",
                    "bye",
                    "three"
                }
            };


            //Act
            dataStore.ExecuteEvent("AddItem", new TestController.AddItemCommand
                {

                });
            dataStore.WriteQueueToDisk().Wait();

            var loadedDataStore = new EventStore<TestController, TestDataType>(controller, x => x.Id.ToString(), fileSystem, dataStoreFile);
            var loadedData = loadedDataStore.Get(insertData.Id.ToString());

            //Assert
            loadedData.Should().BeEquivalentTo(insertData);
        }

        private IFileSystem GetMockFileSystem(string file, string existingBody = "")
        {
            return new MockFileSystem(
                new Dictionary<string, MockFileData>
                {
                    { file, existingBody }
                });
        }

        class TestController
        {
            public void AddItem(IEventStoreAction<TestDataType> datastore, AddItemCommand cmd)
            {
                // using object reference method
                var id = cmd.IdToAddTo;
                var value = datastore.Get(id.ToString());
                value.MyData.Add(cmd.NewItem);

                //datastore.AddOrUpdate(id.ToString(), cmd.NewItem);
                //datastore.Transform(id.ToString(), 
            }

            public TestDataType ModifyItem(IEventStoreAction<TestDataType> datastore, ModifyItemCommand cmd)
            {
                var oldItem = datastore.Get(cmd.Id.ToString());
                oldItem.MyData.Add("Some operation updating shit");
                return oldItem;
            }
            public TestDataType ModifyCloneItem(IEventStoreAction<TestDataType> datastore, ModifyItemCommand cmd)
            {
                //TODO this operation will fuck up things I think!!
                var oldItem = datastore.Get(cmd.Id.ToString());
                oldItem.Id = 12345;
                return oldItem;
            }

            //TODO would be nice to not need a command class for every single one...
            //    Deserializing to parameters is not impossible
            public class AddItemCommand
            {
                public int IdToAddTo { get; set; }
                public string NewItem { get; set; }
            }
            public class ModifyItemCommand
            {
                public int Id { get; set; }
            }
        }
        public class TestDataType
        {
            public int Id { get; set; }

            public List<string> MyData { get; set; }
        }
    }
}