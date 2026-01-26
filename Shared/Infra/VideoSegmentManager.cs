using System;
using System.Collections.Generic;
using System.Linq;
using Wideor.App.Shared.Domain;

namespace Wideor.App.Shared.Infra
{
    public class VideoSegmentManager : IVideoSegmentManager
    {
        private readonly List<VideoSegment> _segments = new();
        private int _nextId = 1;

        public event EventHandler<VideoSegmentEventArgs>? SegmentAdded;
        public event EventHandler<VideoSegmentEventArgs>? SegmentRemoved;
        public event EventHandler<VideoSegmentEventArgs>? SegmentUpdated;

        public IReadOnlyList<VideoSegment> Segments => _segments.AsReadOnly();

        public VideoSegment? GetSegmentById(int id)
        {
            return _segments.FirstOrDefault(s => s.Id == id);
        }

        public List<VideoSegment> GetSegmentsByTimeRange(double startTime, double endTime)
        {
            return _segments.Where(s => s.StartTime < endTime && s.EndTime > startTime).ToList();
        }

        public VideoSegment? GetSegmentAtTime(double time)
        {
            return _segments.FirstOrDefault(s => s.ContainsTime(time));
        }

        public void AddSegment(VideoSegment segment)
        {
            if (segment.Id == 0)
            {
                segment.Id = _nextId++;
            }
            _segments.Add(segment);
            _segments.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));
            
            Wideor.App.Shared.Infra.LogHelper.WriteLog(
                "VideoSegmentManager.cs:AddSegment",
                "VideoSegment added",
                new { segmentId = segment.Id, startTime = segment.StartTime, endTime = segment.EndTime, duration = segment.Duration, videoFilePath = segment.VideoFilePath, segmentCount = _segments.Count });
            
            SegmentAdded?.Invoke(this, new VideoSegmentEventArgs(segment));
        }

        public void RemoveSegment(int id)
        {
            var segment = GetSegmentById(id);
            if (segment != null)
            {
                _segments.Remove(segment);
                SegmentRemoved?.Invoke(this, new VideoSegmentEventArgs(segment));
            }
        }

        public void UpdateSegment(VideoSegment segment)
        {
            var index = _segments.FindIndex(s => s.Id == segment.Id);
            if (index >= 0)
            {
                _segments[index] = segment;
                _segments.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));
                SegmentUpdated?.Invoke(this, new VideoSegmentEventArgs(segment));
            }
        }

        public void Clear()
        {
            _segments.Clear();
            _nextId = 1;
        }
    }
}
