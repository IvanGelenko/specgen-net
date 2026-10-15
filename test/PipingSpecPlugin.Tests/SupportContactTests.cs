using PipingSpecPlugin.Audit;
using Xunit;

namespace PipingSpecPlugin.Tests
{
    public class SupportContactTests
    {
        [Fact]
        public void OnlyAttachedPipingPartIsExcludedInBothPairOrders()
        {
            var support = new AuditPart { Key = "S", IsSupport = true, LineKey = "L" };
            support.SupportedPartKeys.Add("P");
            var attached = new AuditPart { Key = "P", IsPipingComponent = true };
            Assert.True(SupportContactPolicy.IgnoreContact(support, attached));
            Assert.True(SupportContactPolicy.IgnoreContact(attached, support));
            Assert.False(SupportContactPolicy.IgnoreContact(support, new AuditPart { Key = "Other", LineKey = "L", IsPipingComponent = true }));
            attached.IsPipingComponent = false;
            Assert.False(SupportContactPolicy.IgnoreContact(support, attached));
            support.SupportedPartKeys.Clear();
            attached.IsPipingComponent = true;
            Assert.False(SupportContactPolicy.IgnoreContact(support, attached));
        }

        [Fact]
        public void ExternalReferenceInstancesMustNotShareAttachmentKeys()
        {
            var support = new AuditPart { Key = "X1/S", IsSupport = true };
            support.SupportedPartKeys.Add("X1/P");
            Assert.True(SupportContactPolicy.IgnoreContact(support, new AuditPart { Key = "X1/P", IsPipingComponent = true }));
            Assert.False(SupportContactPolicy.IgnoreContact(support, new AuditPart { Key = "X2/P", IsPipingComponent = true }));
            Assert.False(SupportContactPolicy.IgnoreContact(support, new AuditPart { Key = "P", IsPipingComponent = true }));
        }
    }
}
