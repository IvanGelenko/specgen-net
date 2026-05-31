using PipingSpecPlugin.Layers;
using Xunit;

namespace PipingSpecPlugin.Tests
{
    public class TagLayerMoverTests
    {
        [Fact]
        public void ValidTag_PassesThroughUnchanged()
        {
            Assert.Equal("gv-001", TagLayerMover.SanitizeLayerName("gv-001"));
            Assert.Equal("E-205", TagLayerMover.SanitizeLayerName("E-205"));
            Assert.Equal("V_101.A", TagLayerMover.SanitizeLayerName("V_101.A"));
        }

        [Fact]
        public void Tag_IsTrimmed()
        {
            Assert.Equal("gv-001", TagLayerMover.SanitizeLayerName("  gv-001  "));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void EmptyOrWhitespace_ReturnsNull(string raw)
        {
            Assert.Null(TagLayerMover.SanitizeLayerName(raw));
        }

        [Theory]
        // Each forbidden AutoCAD layer-name char becomes '_'.
        [InlineData("a<b", "a_b")]
        [InlineData("a>b", "a_b")]
        [InlineData("a/b", "a_b")]
        [InlineData("a\\b", "a_b")]
        [InlineData("a\"b", "a_b")]
        [InlineData("a:b", "a_b")]
        [InlineData("a;b", "a_b")]
        [InlineData("a?b", "a_b")]
        [InlineData("a*b", "a_b")]
        [InlineData("a|b", "a_b")]
        [InlineData("a,b", "a_b")]
        [InlineData("a=b", "a_b")]
        [InlineData("a`b", "a_b")]
        public void ForbiddenChars_BecomeUnderscore(string raw, string expected)
        {
            Assert.Equal(expected, TagLayerMover.SanitizeLayerName(raw));
        }

        [Fact]
        public void AllForbidden_ReturnsNull()
        {
            // Nothing meaningful left to name a layer after → treat as no-tag.
            // (Result would be "___", which is junk we don't want to create.)
            // Only when every char is forbidden do we bail; a mix keeps going.
            Assert.Null(TagLayerMover.SanitizeLayerName(":::"));
            Assert.Null(TagLayerMover.SanitizeLayerName("..."));
        }

        [Fact]
        public void ControlChars_BecomeUnderscore()
        {
            Assert.Equal("a_b", TagLayerMover.SanitizeLayerName("a\tb"));
            Assert.Equal("a_b", TagLayerMover.SanitizeLayerName("a\nb"));
        }

        [Fact]
        public void OverLongName_IsTruncatedTo255()
        {
            var raw = new string('x', 300);
            var result = TagLayerMover.SanitizeLayerName(raw);
            Assert.NotNull(result);
            Assert.Equal(TagLayerMover.MaxLayerNameLength, result.Length);
        }
    }
}
