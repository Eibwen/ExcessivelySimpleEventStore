using ExcessivelySimpleEventStore;
using FluentAssertions;
using NUnit.Framework;

namespace ExcessivelySimpleEventStoreTests
{
    public class Tests
    {
        [Test]
        public void Test1()
        {
            Assert.Pass();
        }

        [Test]
        public void Test_project_references_main_project()
        {
            var cls = new Class1();
            cls.Square(5).Should().Be(25);
        }
    }
}