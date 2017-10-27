using System;
using Xunit;

namespace SamplesTest
{
    public class SamplesTestRun
    {
        [Fact]
        public void AutoForwardTest()
        {
            Assert.Equal(0, AutoForward.Program.Main(new string[0]));
        }

        [Fact]
        public void DuplicateDetectionTest()
        {
            Assert.Equal(0, DuplicateDetection.Program.Main(new string[0]));
        }

        [Fact]
        public void MessageBrowseTest()
        {
            Assert.Equal(0, MessageBrowse.Program.Main(new string[0]));
        }

        [Fact]
        public void PrefetchTest()
        {
            Assert.Equal(0, Prefetch.Program.Main(new string[0]));
        }

        [Fact]
        public void ReceiveLoopTest()
        {
            Assert.Equal(0, ReceiveLoop.Program.Main(new string[0]));
        }

        [Fact]
        public void SendersReceiversWithQueuesTest()
        {
            Assert.Equal(0, SendersReceiversWithQueues.Program.Main(new string[0]));
        }

        [Fact]
        public void SendersReceiversWithTopicsTest()
        {
            Assert.Equal(0, SendersReceiversWithTopics.Program.Main(new string[0]));
        }

        [Fact]
        public void TimeToLiveTest()
        {
            Assert.Equal(0, TimeToLive.Program.Main(new string[0]));
        }

        [Fact]
        public void DeadletterQueueTest()
        {
            Assert.Equal(0, DeadletterQueue.Program.Main(new string[0]));
        }

        [Fact]
        public void GeoReplicationActiveTest()
        {
            Assert.Equal(0, GeoSenderActiveReplication.Program.Main(new string[0]));
            Assert.Equal(0, GeoReceiver.Program.Main(new string[0]));
        }

        [Fact]
        public void GeoReplicationPassiveTest()
        {
            Assert.Equal(0, GeoSenderPassiveReplication.Program.Main(new string[0]));
            Assert.Equal(0, GeoReceiver.Program.Main(new string[0]));
        }

        [Fact]
        public void QueuesGettingStartedTest()
        {
            Assert.Equal(0, QueuesGettingStarted.Program.Main(new string[0]));
        }

        [Fact]
        public void TopicsGettingStartedTest()
        {
            Assert.Equal(0, TopicsGettingStarted.Program.Main(new string[0]));
        }

        [Fact]
        public void PartitionedQueuesTest()
        {
            Assert.Equal(0, PartitionedQueues.Program.Main(new string[0]));
        }

        [Fact]
        public void DeferralTest()
        {
            Assert.Equal(0, Deferral.Program.Main(new string[0]));
        }

        [Fact]
        public void SessionsTest()
        {
            Assert.Equal(0, Sessions.Program.Main(new string[0]));
        }

        [Fact(Skip = "Platform bug")]
        public void SessionStateTest()
        {
            Assert.Equal(0, SessionState.Program.Main(new string[0]));
        }

        [Fact]
        public void PrioritySubscriptionsTest()
        {
            Assert.Equal(0, PrioritySubscriptions.Program.Main(new string[0]));
        }

        [Fact]
        public void TopicFiltersTest()
        {
            Assert.Equal(0, TopicFilters.Program.Main(new string[0]));
        }
    }
}
