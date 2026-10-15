using PipingSpecPlugin.Spec;
using Xunit;

namespace PipingSpecPlugin.Tests
{
    public class LineDesignationParserTests
    {
        [Fact]
        public void Parses_Full_Designation_With_Flange_Segment()
        {
            var parts = LineDesignationParser.Parse("150-KSN001-LS016C-HN");
            Assert.NotNull(parts);
            Assert.Equal("150", parts.NominalSize);
            Assert.Equal("KSN", parts.EnvironmentCode);
            Assert.Equal("001", parts.LineNumber);
            Assert.Equal("LS", parts.MaterialCode);
            Assert.Equal("016", parts.PressureCode);
            Assert.Equal("C", parts.FlangeCode);
            Assert.Equal("H", parts.InsulationCode);
            Assert.Equal("N", parts.SputnikCode);
        }

        [Fact]
        public void Parses_Designation_With_Trailing_Comment()
        {
            var parts = LineDesignationParser.Parse("150-KSN001-LS016C-HN (Врезка 2; 5)");
            Assert.NotNull(parts);
            Assert.Equal("150", parts.NominalSize);
            Assert.Equal("N", parts.SputnikCode);
        }

        [Fact]
        public void Parses_Designation_Without_Optional_Flange_Segment()
        {
            var parts = LineDesignationParser.Parse("050-N001-LS016B-NN");
            Assert.NotNull(parts);
            Assert.Equal("050", parts.NominalSize);
            Assert.Equal("N", parts.EnvironmentCode);
            Assert.Equal("LS", parts.MaterialCode);
            Assert.Equal("016", parts.PressureCode);
            Assert.Equal("B", parts.FlangeCode); // single letter before '-' is always captured as flange, not optional-absent here
            Assert.Equal("N", parts.InsulationCode);
            Assert.Equal("N", parts.SputnikCode);
        }

        [Fact]
        public void Takes_Only_First_Line_Of_MultiId_Cell()
        {
            var parts = LineDesignationParser.Parse(
                "050-FL001-LS016C-NN (см. Ф-1/1; Ф-1/2)\n050-FL002-LS016C-NN (см. Ф-1/1; Ф-1/2)");
            Assert.NotNull(parts);
            Assert.Equal("FL", parts.EnvironmentCode);
        }

        [Fact]
        public void Returns_Null_For_Legacy_Short_Tag_Format()
        {
            Assert.Null(LineDesignationParser.Parse("P-22002"));
            Assert.Null(LineDesignationParser.Parse("150-P22002/2-LS025C")); // missing insulation/sputnik segment
        }

        [Fact]
        public void Parses_Line_Number_With_Slash_Sub_Number()
        {
            // Real catalogue example: environment code is still just the
            // leading letters ("P"), the rest of that segment ("22002/2")
            // is free-form and not synced to any field.
            var parts = LineDesignationParser.Parse("150-P22002/2-LS025C-HN");
            Assert.NotNull(parts);
            Assert.Equal("150", parts.NominalSize);
            Assert.Equal("P", parts.EnvironmentCode);
            Assert.Equal("22002/2", parts.LineNumber);
            Assert.Equal("LS", parts.MaterialCode);
            Assert.Equal("025", parts.PressureCode);
            Assert.Equal("C", parts.FlangeCode);
            Assert.Equal("H", parts.InsulationCode);
            Assert.Equal("N", parts.SputnikCode);
        }

        [Fact]
        public void Returns_Null_For_Empty_Or_Whitespace()
        {
            Assert.Null(LineDesignationParser.Parse(""));
            Assert.Null(LineDesignationParser.Parse("   "));
            Assert.Null(LineDesignationParser.Parse(null));
        }

        [Theory]
        [InlineData("150-KSN001-LS016C-HN (Врезка 2; 5)", "150-KSN001-LS016C-HN")]
        [InlineData("150-KSN001-LS016C-HN(Врезка 2; 5)", "150-KSN001-LS016C-HN")]
        [InlineData("050-FL001-LS016C-NN (см. Ф-1/1; Ф-1/2)\n050-FL002-LS016C-NN (см. Ф-1/1; Ф-1/2)", "050-FL001-LS016C-NN")]
        [InlineData("150-KSN001-LS016C-HN\r\n150-KSN002-LS016C-HN", "150-KSN001-LS016C-HN")]
        [InlineData("150-KSN001-LS016C-HN (a (b) c)", "150-KSN001-LS016C-HN")]
        [InlineData("150-KSN001-LS016C-HN (без закрывающей скобки", "150-KSN001-LS016C-HN")]
        [InlineData("150-KSN001-LS016C-HN (x) (y)", "150-KSN001-LS016C-HN")]
        [InlineData("  P-22002  ", "P-22002")]
        [InlineData("(Резерв)", "")]
        [InlineData("", "")]
        [InlineData(null, "")]
        public void TagOf_Drops_Parenthesised_Remarks(string designation, string expected)
        {
            Assert.Equal(expected, LineDesignationParser.TagOf(designation));
        }

        [Fact]
        public void Codes_Are_Normalized_To_Upper_Invariant()
        {
            var parts = LineDesignationParser.Parse("150-ksn001-ls016c-hn");
            Assert.NotNull(parts);
            Assert.Equal("KSN", parts.EnvironmentCode);
            Assert.Equal("LS", parts.MaterialCode);
            Assert.Equal("H", parts.InsulationCode);
            Assert.Equal("N", parts.SputnikCode);
        }
    }
}
