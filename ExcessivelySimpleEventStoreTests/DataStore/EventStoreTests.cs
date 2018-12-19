using System.Collections.Generic;
using System.IO.Abstractions;
using ExcessivelySimpleEventStore.DataStore;
using NUnit.Framework;

namespace ExcessivelySimpleEventStoreTests.DataStore
{
    [TestFixture]
    public class EventStoreTests
    {
        [Test]
        public void When_()
        {
            //Arrange
            var fileSystem = new FileSystem();

            var controller = new TestController();
            var dataStoreFile = fileSystem.Path.Combine("fakePath", "TestDataStore.db");
            var dataStore = new EventStore<TestController, TestDataType>(controller, x => x.Id.ToString(), fileSystem, dataStoreFile);


            //Act
            dataStore

            //Assert

        }

        class TestController
        {
            public void AddItem(EventStore<TestController, TestDataType> datastore, AddItemCommand cmd)
            {
                // using object reference method
                var id = cmd.IdToAddTo;
                var value = datastore.Get(id.ToString());
                value.MyData.Add(cmd.NewItem);

                //datastore.AddOrUpdate(id.ToString(), cmd.NewItem);
                //datastore.Transform(id.ToString(), 
            }

            public TestDataType ModifyItem(EventStore<TestController, TestDataType> datastore, ModifyItemCommand cmd)
            {
                var oldItem = datastore.Get(cmd.Id.ToString());
                oldItem.MyData.Add("Some operation updating shit");
                return oldItem;
            }
            public TestDataType ModifyCloneItem(EventStore<TestController, TestDataType> datastore, ModifyItemCommand cmd)
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