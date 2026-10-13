using IPSwitch.Services;
using Xunit;

namespace IPSwitch.Tests
{
    /// <summary>Tests of the shared adapter snapshot (<see cref="NetworkService.GetAllInterfaces"/>).</summary>
    public class AdapterSnapshotTests
    {
        [Fact]
        public void ConsecutiveQueries_ShareTheSnapshot()
        {
            NetworkService.InvalidateSnapshot();

            var first = NetworkService.GetAllInterfaces();
            var second = NetworkService.GetAllInterfaces();

            Assert.Same(first, second);
        }

        [Fact]
        public void Invalidate_ForcesAFreshEnumeration()
        {
            var before = NetworkService.GetAllInterfaces();

            NetworkService.InvalidateSnapshot();
            var after = NetworkService.GetAllInterfaces();

            Assert.NotSame(before, after);
        }
    }
}
