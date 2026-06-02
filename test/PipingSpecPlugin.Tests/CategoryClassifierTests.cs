using PipingSpecPlugin.Spec;
using Xunit;

namespace PipingSpecPlugin.Tests
{
    public class CategoryClassifierTests
    {
        [Fact]
        public void IsoSymbolTypeSupport_ClassifiesAsSupports()
        {
            var r = new PartRecord
            {
                PnPClassName = "PipeSupport",
                ContentIsoSymbolDefinition = "...;TYPE=SUPPORT;...",
            };
            Assert.Equal("Supports", CategoryClassifier.Normalize(r));
        }

        // The bug: supports modelled as StructureMember have no
        // TYPE=SUPPORT iso symbol and an empty PartCategory, so they used
        // to drop out as empty-category — only the one real Pipe Support
        // on a line made it into the spec. Now they're recognised by their
        // "Опора …" description.
        [Theory]
        [InlineData("Опора 133-КП-А11-ВСт3пс")]
        [InlineData("опора скользящая")]
        [InlineData("Pipe Support, welded")]
        public void StructureMemberSupport_DetectedByDescription(string desc)
        {
            var r = new PartRecord
            {
                PnPClassName = "StructureMember",
                PartCategory = null,
                ContentIsoSymbolDefinition = null,
                ShortDescription = desc,
            };
            Assert.Equal("Supports", CategoryClassifier.Normalize(r));
        }

        [Fact]
        public void StructureMemberWithoutSupportKeyword_StaysEmpty()
        {
            var r = new PartRecord
            {
                PnPClassName = "StructureMember",
                PartCategory = null,
                ShortDescription = "Балка двутавровая 20Б1",
            };
            Assert.Equal("", CategoryClassifier.Normalize(r));
        }

        [Fact]
        public void NonEmptyPartCategory_WinsOverDescription()
        {
            // An explicit category must not be overridden by a keyword
            // match in the description.
            var r = new PartRecord
            {
                PartCategory = "Fittings",
                ShortDescription = "Тройник на опоре",
            };
            Assert.Equal("Fittings", CategoryClassifier.Normalize(r));
        }
    }
}
