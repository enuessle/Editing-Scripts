import subprocess
import re
import os
import sys
import tempfile
import argparse

def detect_silence(input_file: str, noise: str = "-50dB", duration: str = "0.5", audio_track: int = 0):
    """
    Run ffmpeg with the silencedetect filter on a specific audio track (by relative order)
    to detect silence intervals.
    Returns a list of tuples: (silence_start, silence_end).
    """
    cmd = [
        "ffmpeg",
        "-i", input_file,
        "-map", f"0:a:{audio_track}",
        "-af", f"silencedetect=noise={noise}:d={duration}",
        "-f", "null", "-"
    ]
    print("Running ffmpeg for silence detection...")
    result = subprocess.run(cmd, stderr=subprocess.PIPE, stdout=subprocess.PIPE, text=True)
    output = result.stderr

    silence_starts = []
    silence_ends = []
    for line in output.splitlines():
        if "silence_start" in line:
            m = re.search(r"silence_start:\s?(\d+\.?\d*)", line)
            if m:
                silence_starts.append(float(m.group(1)))
        if "silence_end" in line:
            m = re.search(r"silence_end:\s?(\d+\.?\d*)", line)
            if m:
                silence_ends.append(float(m.group(1)))
    intervals = []
    for i in range(min(len(silence_starts), len(silence_ends))):
        intervals.append((silence_starts[i], silence_ends[i]))
    return intervals

def get_video_duration(input_file: str) -> float:
    """
    Uses ffprobe to get the duration of the video (in seconds).
    """
    cmd = [
        "ffprobe",
        "-i", input_file,
        "-show_entries", "format=duration",
        "-v", "quiet",
        "-of", "csv=p=0"
    ]
    result = subprocess.run(cmd, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    duration_str = result.stdout.strip()
    try:
        return float(duration_str)
    except ValueError:
        print("Error obtaining video duration.")
        sys.exit(1)

def get_audio_track_count(input_file: str) -> int:
    """
    Uses ffprobe to count how many audio streams exist in the input file.
    """
    cmd = [
        "ffprobe",
        "-v", "error",
        "-select_streams", "a",
        "-show_entries", "stream=index",
        "-of", "csv=p=0",
        input_file
    ]
    result = subprocess.run(cmd, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    lines = [line.strip() for line in result.stdout.splitlines() if line.strip()]
    return len(lines)

def compute_nonsilent_intervals(silence_intervals, video_duration: float, min_gap: float = 0.1):
    """
    Invert the silence intervals into non-silent intervals.
    """
    intervals = []
    prev_end = 0.0
    for silence in silence_intervals:
        start, end = silence
        if start - prev_end > min_gap:
            intervals.append((prev_end, start))
        prev_end = end
    if video_duration - prev_end > min_gap:
        intervals.append((prev_end, video_duration))
    return intervals

def cut_video_segments(input_file: str, intervals, temp_dir: str, reencode: bool):
    """
    For each non-silent interval, extract that segment from the input file.
    
    If reencode is False, use input seeking (‑ss before ‑i and -t for duration) along with
    explicit mapping to preserve all streams. This mode is fast but relies on keyframe positioning.
    
    If reencode is True, the segment is re‑encoded to generate a new time base.
    
    Returns a list of paths to the segmented files.
    """
    segment_files = []
    for idx, (start, end) in enumerate(intervals):
        output_segment = os.path.join(temp_dir, f"segment_{idx}.mp4")
        duration = end - start
        if reencode:
            # Re-encode mode: re-encodes video and audio so that all streams get freshly time-stamped.
            cmd = [
                "ffmpeg",
                "-y",              # Overwrite output.
                "-i", input_file,
                "-ss", f"{start}",
                "-to", f"{end}",
                "-map", "0",       # Map every stream.
                "-c:v", "libx264",
                "-preset", "veryfast",
                "-crf", "18",
                "-c:a", "aac",
                "-b:a", "192k",
                output_segment
            ]
        else:
            # Stream copy mode using input seeking:
            # Place -ss before -i and use -t to extract exact duration.
            # Explicitly map video and all audio streams.
            cmd = [
                "ffmpeg",
                "-y",
                "-ss", f"{start}",  # Input seeking: seek to the start time before reading.
                "-i", input_file,
                "-t", f"{duration}", # Extract for the calculated duration.
                "-map", "0:v",       # Map the video stream.
                "-map", "0:a",       # Map all audio streams.
                "-c", "copy",
                "-movflags", "+faststart",  # Optimize for playback.
                "-fflags", "+genpts",       # Generate new presentation timestamps.
                output_segment
            ]
        print(f"Cutting segment {idx}: {start:.3f} to {end:.3f} (duration {duration:.3f})...")
        subprocess.run(cmd, check=True)
        segment_files.append(output_segment)
    return segment_files


def concat_segments(segment_files, output_file: str, temp_dir: str):
    """
    Concatenates segmented video files into one output file using ffmpeg's concat demuxer.
    
    Here we write a temporary file list and explicitly map the video and audio streams.
    """
    list_file = os.path.join(temp_dir, "segments.txt")
    with open(list_file, "w", encoding="utf-8") as f:
        for seg in segment_files:
            # Ensure correct path formatting in the file list.
            f.write(f"file '{seg}'\n")
    cmd = [
        "ffmpeg",
        "-y",
        "-f", "concat",
        "-safe", "0",
        "-i", list_file,
        "-map", "0:v",   # Map video stream from each segment.
        "-map", "0:a",   # Map all audio streams from each segment.
        "-c", "copy",
        "-movflags", "+faststart",
        output_file
    ]
    print("Concatenating segments...")
    subprocess.run(cmd, check=True)

def main(args):
    input_file = args.input
    output_file = args.output
    noise = args.noise
    duration = args.duration
    audio_track = args.audio_track
    reencode = args.reencode

    # Detect silence intervals on the selected audio track.
    silence_intervals = detect_silence(input_file, noise=noise, duration=duration, audio_track=audio_track)
    print("Detected silence intervals:")
    for interval in silence_intervals:
        print(interval)

    # Get total video duration.
    video_duration = get_video_duration(input_file)
    print(f"Video duration: {video_duration} seconds")

    # Compute non-silent intervals.
    nonsilent_intervals = compute_nonsilent_intervals(silence_intervals, video_duration)
    print("Non-silent intervals to keep:")
    for interval in nonsilent_intervals:
        print(interval)

    if not nonsilent_intervals:
        print("No non-silent intervals detected.")
        sys.exit(1)

    # Create a temporary directory to store segments.
    with tempfile.TemporaryDirectory() as temp_dir:
        segment_files = cut_video_segments(input_file, nonsilent_intervals, temp_dir, reencode)
        concat_segments(segment_files, output_file, temp_dir)

    print(f"Output video written to {output_file}")

if __name__ == "__main__":
    parser = argparse.ArgumentParser(
        description="Cut out silence from a video using ffmpeg (based on analysis of one audio track) and produce a new video that preserves all input streams (all audio tracks, video, etc.)."
    )
    parser.add_argument("input", type=str, help="Path to the input video file.")
    parser.add_argument("output", type=str, help="Path to the output video file.")
    parser.add_argument("--noise", type=str, default="-50dB", help="Noise threshold for silence detection (default: -50dB).")
    parser.add_argument("--duration", type=str, default="0.5", help="Minimum silence duration in seconds (default: 0.5).")
    parser.add_argument("--audio-track", type=int, default=0, help="Audio track index (relative) to use for silence detection (default: 0).")
    parser.add_argument("--reencode", action="store_true",
                        help="Re-encode segments to preserve all streams. If not specified, the script uses stream copy.")
    args = parser.parse_args()
    main(args)
