using System;
using System.Collections.Generic;
using Wideor.App.Shared.Domain;

namespace Wideor.App.Shared.Infra
{
    public interface IVideoSegmentManager
    {
        event EventHandler<VideoSegmentEventArgs>? SegmentAdded;
        event EventHandler<VideoSegmentEventArgs>? SegmentRemoved;
        event EventHandler<VideoSegmentEventArgs>? SegmentUpdated;
        
        IReadOnlyList<VideoSegment> Segments { get; }
        VideoSegment? GetSegmentById(int id);
        List<VideoSegment> GetSegmentsByTimeRange(double startTime, double endTime);
        VideoSegment? GetSegmentAtTime(double time);
        
        void AddSegment(VideoSegment segment);
        void RemoveSegment(int id);
        void UpdateSegment(VideoSegment segment);
        void Clear();
    }

    public class VideoSegmentEventArgs : EventArgs
    {
        public VideoSegment Segment { get; }
        public VideoSegmentEventArgs(VideoSegment segment) => Segment = segment;
    }
}
