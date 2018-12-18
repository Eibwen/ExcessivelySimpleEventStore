using ExcessivelySimpleEventStore;
using FluentAssertions;
using NUnit.Framework;

namespace ExcessivelySimpleEventStoreTests
{
    public class Tests
    {
        [SetUp]
        public void Setup()
        {
        }

        [Test]
        public void Test1()
        {
            Assert.Pass();
        }

        [Test]
        public void When_()
        {
            var cls = new Class1();
            cls.Square(5).Should().Be(25);
        }
    }
}