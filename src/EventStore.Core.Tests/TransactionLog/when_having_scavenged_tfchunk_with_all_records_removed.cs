using System;
using System.Collections.Generic;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Core.Helpers;
using EventStore.Core.Index.Hashes;
using EventStore.Core.Messaging;
using EventStore.Core.Tests.Services.Storage;
using EventStore.Core.Tests.Fakes;
using EventStore.Core.TransactionLog;
using EventStore.Core.TransactionLog.Checkpoint;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.TransactionLog.Chunks.TFChunk;
using EventStore.Core.TransactionLog.FileNamingStrategy;
using EventStore.Core.TransactionLog.LogRecords;
using NUnit.Framework;

namespace EventStore.Core.Tests.TransactionLog
{
    [TestFixture]
    public class when_having_scavenged_tfchunk_with_all_records_removed: SpecificationWithDirectoryPerTestFixture
    {
        private TFChunkDb _db;
        private TFChunk _scavengedChunk;
        private PrepareLogRecord _p1, _p2, _p3;
        private CommitLogRecord _c1, _c2, _c3;
        private RecordWriteResult _res1, _res2, _res3;
        private RecordWriteResult _cres1, _cres2, _cres3;

        public override void TestFixtureSetUp()
        {
            base.TestFixtureSetUp();

            _db = new TFChunkDb(new TFChunkDbConfig(PathName,
                                                    new VersionedPatternFileNamingStrategy(PathName, "chunk-"),
                                                    16 * 1024,
                                                    0,
                                                    new InMemoryCheckpoint(),
                                                    new InMemoryCheckpoint(),
                                                    new InMemoryCheckpoint(-1),
                                                    new InMemoryCheckpoint(-1)));
            _db.Open();
            
            var chunk = _db.Manager.GetChunkFor(0);

            _p1 = LogRecord.SingleWrite(0, Guid.NewGuid(), Guid.NewGuid(), "es-to-scavenge", ExpectedVersion.Any, "et1",
                                          new byte[] { 0, 1, 2 }, new byte[] { 5, 7 });
            _res1 = chunk.TryAppend(_p1);

            _c1 = LogRecord.Commit(_res1.NewPosition, Guid.NewGuid(), _p1.LogPosition, 0);
            _cres1 = chunk.TryAppend(_c1);

            _p2 = LogRecord.SingleWrite(_cres1.NewPosition,
                                        Guid.NewGuid(), Guid.NewGuid(), "es-to-scavenge", ExpectedVersion.Any, "et1",
                                        new byte[] { 0, 1, 2 }, new byte[] { 5, 7 });
            _res2 = chunk.TryAppend(_p2);

            _c2 = LogRecord.Commit(_res2.NewPosition, Guid.NewGuid(), _p2.LogPosition, 1);
            _cres2 = chunk.TryAppend(_c2);
            
            _p3 = LogRecord.SingleWrite(_cres2.NewPosition,
                                        Guid.NewGuid(), Guid.NewGuid(), "es-to-scavenge", ExpectedVersion.Any, "et1",
                                        new byte[] { 0, 1, 2 }, new byte[] { 5, 7 });
            _res3 = chunk.TryAppend(_p3);

            _c3 = LogRecord.Commit(_res3.NewPosition, Guid.NewGuid(), _p3.LogPosition, 2);
            _cres3 = chunk.TryAppend(_c3);

            chunk.Complete();

            _db.Config.WriterCheckpoint.Write(chunk.ChunkHeader.ChunkEndPosition);
            _db.Config.WriterCheckpoint.Flush();
            _db.Config.ChaserCheckpoint.Write(chunk.ChunkHeader.ChunkEndPosition);
            _db.Config.ChaserCheckpoint.Flush();

            var bus = new InMemoryBus("Bus");
            var ioDispatcher = new IODispatcher(bus, new PublishEnvelope(bus));
            var scavenger = new TFChunkScavenger(_db, ioDispatcher, new FakeTableIndex(), new XXHashUnsafe(),
                                                 new FakeReadIndex(x => x == "es-to-scavenge"), Guid.NewGuid(), "fakeNodeIp");
            scavenger.Scavenge(alwaysKeepScavenged: true, mergeChunks: false);

            _scavengedChunk = _db.Manager.GetChunk(0);
        }

        public override void TestFixtureTearDown()
        {
            _db.Dispose();

            base.TestFixtureTearDown();
        }

        [Test]
        public void first_record_was_written()
        {
            Assert.IsTrue(_res1.Success);
            Assert.IsTrue(_cres1.Success);
        }

        [Test]
        public void second_record_was_written()
        {
            Assert.IsTrue(_res2.Success);
            Assert.IsTrue(_cres2.Success);
        }

        [Test]
        public void third_record_was_written()
        {
            Assert.IsTrue(_res3.Success);
            Assert.IsTrue(_cres3.Success);
        }

        [Test]
        public void prepare1_cant_be_read_at_position()
        {
            var res = _scavengedChunk.TryReadAt((int)_p1.LogPosition);
            Assert.IsFalse(res.Success);
        }

        [Test]
        public void commit1_cant_be_read_at_position()
        {
            var res = _scavengedChunk.TryReadAt((int)_c1.LogPosition);
            Assert.IsFalse(res.Success);
        }

        [Test]
        public void prepare2_cant_be_read_at_position()
        {
            var res = _scavengedChunk.TryReadAt((int)_p2.LogPosition);
            Assert.IsFalse(res.Success);
        }

        [Test]
        public void commit2_cant_be_read_at_position()
        {
            var res = _scavengedChunk.TryReadAt((int)_c2.LogPosition);
            Assert.IsFalse(res.Success);
        }

        [Test]
        public void prepare3_cant_be_read_at_position()
        {
            var res = _scavengedChunk.TryReadAt((int)_p3.LogPosition);
            Assert.IsFalse(res.Success);
        }

        [Test]
        public void commit3_cant_be_read_at_position()
        {
            var res = _scavengedChunk.TryReadAt((int)_c3.LogPosition);
            Assert.IsFalse(res.Success);
        }

        [Test]
        public void sequencial_read_returns_no_records()
        {
            var records = new List<LogRecord>();
            RecordReadResult res = _scavengedChunk.TryReadFirst();
            while (res.Success)
            {
                records.Add(res.LogRecord);
                res = _scavengedChunk.TryReadClosestForward((int)res.NextPosition);
            }
            Assert.AreEqual(0, records.Count);
        }
    }
}