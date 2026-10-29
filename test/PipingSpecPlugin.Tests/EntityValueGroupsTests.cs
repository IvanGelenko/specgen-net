using System.Collections.Generic;
using PipingSpecPlugin.Spec;
using Xunit;

namespace PipingSpecPlugin.Tests
{
    // SELECTBYSTATUS / SELECTBYLINE group entities, not rows: a weld is a
    // P3dConnector row without Status plus a Buttweld row with it.
    public class EntityValueGroupsTests
    {
        const string Null = "__NULL__";
        static (string, IReadOnlyCollection<long>) Row(string value, params long[] handles) => (value, handles);

        [Fact]
        public void AWeldTakesTheStatusOfItsSubPart()
        {
            var groups = EntityValueGroups.Build(new[]
            {
                Row("Новое", 0x10),           // pipe
                Row(null, 0x20),              // weld: P3dConnector row
                Row("Новое", 0x20),           // weld: Buttweld row, same entity
            }, Null);
            Assert.False(groups.ContainsKey(Null));
            Assert.Equal(new long[] { 0x10, 0x20 }, groups["Новое"].Handles);
            Assert.Equal(2, groups["Новое"].Count);
        }

        [Fact]
        public void AnEntityIsEmptyOnlyWhenAllItsRowsAre()
        {
            var groups = EntityValueGroups.Build(new[] { Row(null, 0x20), Row(" ", 0x20), Row("Существующее", 0x30) }, Null);
            Assert.Equal(new long[] { 0x20 }, groups[Null].Handles);
            Assert.Equal(1, groups[Null].Count);
        }

        [Fact]
        public void SubPartsOfDifferentStatusListTheEntityUnderEach()
        {
            // A flanged joint: gasket existing, bolts new.
            var groups = EntityValueGroups.Build(new[] { Row(null, 0x40), Row("Существующее", 0x40), Row("Новое", 0x40) }, Null);
            Assert.Equal(new long[] { 0x40 }, groups["Существующее"].Handles);
            Assert.Equal(new long[] { 0x40 }, groups["Новое"].Handles);
        }

        [Fact]
        public void ARowWithoutEntityIsStillCounted()
        {
            var groups = EntityValueGroups.Build(new[] { Row("Новое"), Row("Новое", 0x10) }, Null);
            Assert.Equal(2, groups["Новое"].Count);
            Assert.Equal(new long[] { 0x10 }, groups["Новое"].Handles);
        }
    }
}
