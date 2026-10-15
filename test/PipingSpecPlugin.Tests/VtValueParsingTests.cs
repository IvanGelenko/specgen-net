using PipingSpecPlugin.Spec;
using Xunit;

namespace PipingSpecPlugin.Tests
{
    public class VtValueParsingTests
    {
        [Theory]
        [InlineData("2,0/1,6", "2", "1.6")]
        [InlineData("1,5/1,2", "1.5", "1.2")]
        [InlineData("0,86/0,002", "0.86", "0.002")]
        [InlineData("1,4*/0,6", "1.4", "0.6")] // footnote asterisk on the first number
        public void SplitTestValues_Splits_Strength_And_Tightness(string cell, string expectedD, string expectedW)
        {
            var (testD, testW) = VtValueParsing.SplitTestValues(cell);
            Assert.Equal(expectedD, testD);
            Assert.Equal(expectedW, testW);
        }

        [Fact]
        public void SplitTestValues_Handles_Single_Number_Without_Slash()
        {
            var (testD, testW) = VtValueParsing.SplitTestValues("1,6");
            Assert.Equal("1.6", testD);
            Assert.Null(testW);
        }

        [Fact]
        public void SplitTestValues_Returns_Nulls_For_Blank_Cell()
        {
            var (testD, testW) = VtValueParsing.SplitTestValues("");
            Assert.Null(testD);
            Assert.Null(testW);
            var (testD2, testW2) = VtValueParsing.SplitTestValues(null);
            Assert.Null(testD2);
            Assert.Null(testW2);
        }

        [Fact]
        public void SplitTestValues_Returns_Null_For_Nonnumeric_Segment()
        {
            var (testD, testW) = VtValueParsing.SplitTestValues("—/1,6");
            Assert.Null(testD);
            Assert.Equal("1.6", testW);
        }

        [Theory]
        [InlineData("РД/УЗК 10%", "10")]
        [InlineData("РД/УЗК 20%", "20")]
        [InlineData("100%", "100")]
        public void ExtractNdPercent_Pulls_Number_Before_Percent_Sign(string cell, string expected)
        {
            Assert.Equal(expected, VtValueParsing.ExtractNdPercent(cell));
        }

        [Fact]
        public void ExtractNdPercent_Returns_Null_When_No_Percent_Found()
        {
            Assert.Null(VtValueParsing.ExtractNdPercent("см. примечание"));
            Assert.Null(VtValueParsing.ExtractNdPercent(""));
            Assert.Null(VtValueParsing.ExtractNdPercent(null));
        }

        [Theory]
        [InlineData("20/2", "20")]
        [InlineData("20", "20")]
        [InlineData("15,5/1", "15.5")]
        public void ExtractServiceLifeYears_Takes_First_Number_Of_Composite_Cell(string cell, string expected)
        {
            Assert.Equal(expected, VtValueParsing.ExtractServiceLifeYears(cell));
        }

        [Fact]
        public void ExtractServiceLifeYears_Returns_Null_For_Blank_Or_Nonnumeric()
        {
            Assert.Null(VtValueParsing.ExtractServiceLifeYears(""));
            Assert.Null(VtValueParsing.ExtractServiceLifeYears(null));
            Assert.Null(VtValueParsing.ExtractServiceLifeYears("н/д"));
        }

        [Theory]
        [InlineData("0.1", "20/2", "2")]
        [InlineData("0,1", "20", "2")]
        [InlineData("0.5", "15,5/1", "7.75")]
        public void ComputeCorrosionAllowance_Multiplies_Rate_By_Service_Life_Years(
            string rateCell, string serviceLifeCell, string expected)
        {
            Assert.Equal(expected, VtValueParsing.ComputeCorrosionAllowance(rateCell, serviceLifeCell));
        }

        [Fact]
        public void ComputeCorrosionAllowance_Returns_Null_When_Either_Input_Missing()
        {
            Assert.Null(VtValueParsing.ComputeCorrosionAllowance(null, "20/2"));
            Assert.Null(VtValueParsing.ComputeCorrosionAllowance("0.1", null));
            Assert.Null(VtValueParsing.ComputeCorrosionAllowance("н/д", "20/2"));
            Assert.Null(VtValueParsing.ComputeCorrosionAllowance("0.1", "н/д"));
        }

        [Theory]
        [InlineData("1.9", "1.10")] // "1.10" sorts after "1.9" numerically, not "1.10" < "1.2" lexically
        [InlineData("1.2", "1.10")]
        [InlineData("1", "2")]
        [InlineData("9", "10")]
        [InlineData("1.1", "1.2")]
        [InlineData("3.1", "3.2")]
        public void NaturalCompare_Orders_Digit_Runs_Numerically(string smaller, string larger)
        {
            Assert.True(VtValueParsing.NaturalCompare(smaller, larger) < 0);
            Assert.True(VtValueParsing.NaturalCompare(larger, smaller) > 0);
        }

        [Fact]
        public void NaturalCompare_Sorts_A_Realistic_Id_List_In_Section_Order()
        {
            var ids = new[] { "1.10", "1.2", "1.1", "1.9", "1.13", "1.3" };
            System.Array.Sort(ids, VtValueParsing.NaturalCompare);
            Assert.Equal(new[] { "1.1", "1.2", "1.3", "1.9", "1.10", "1.13" }, ids);
        }

        [Fact]
        public void NaturalCompare_Falls_Back_To_Ordinal_For_NonNumeric_Ids()
        {
            Assert.True(VtValueParsing.NaturalCompare("asdd", "gddf") < 0);
            Assert.Equal(0, VtValueParsing.NaturalCompare("asdd", "asdd"));
        }

        [Fact]
        public void NaturalCompare_Treats_Null_As_Smallest()
        {
            Assert.True(VtValueParsing.NaturalCompare(null, "1") < 0);
            Assert.True(VtValueParsing.NaturalCompare("1", null) > 0);
            Assert.Equal(0, VtValueParsing.NaturalCompare(null, null));
        }
    }
}
