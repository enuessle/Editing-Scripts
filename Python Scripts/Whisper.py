import argparse
import os
import subprocess
import sys
import tempfile
import whisper

def get_audio_track_count(input_file: str) -> int:
    """
    Use ffprobe to count how many audio streams exist in the input file.
    Returns an integer count.
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

def extract_audio_track(input_file: str, audio_index: int, output_audio: str) -> None:
    """
    Use ffmpeg to extract a single audio track (by relative index) from the video.
    The audio is written as a WAV file (mono, 16 kHz) for compatibility with Whisper.
    """
    cmd = [
        "ffmpeg",
        "-y",                       # Overwrite if exists.
        "-i", input_file,
        "-map", f"0:a:{audio_index}",
        "-ac", "1",                 # Force mono.
        "-ar", "16000",             # 16 kHz sample rate (default for Whisper).
        output_audio
    ]
    print(f"Extracting audio track {audio_index} to {output_audio}...")
    subprocess.run(cmd, check=True)

def export_srt(transcription: dict, output_srt: str) -> None:
    """
    Exports the Whisper transcription result (a dict with a 'segments' key)
    to an SRT file.
    """
    srt_lines = []
    # Iterate over segments and build SRT entries.
    for index, segment in enumerate(transcription.get("segments", [])):
        start_time = format_timestamp(segment["start"])
        end_time = format_timestamp(segment["end"])
        text = segment["text"].strip()
        srt_lines.append(f"{index+1}")
        srt_lines.append(f"{start_time} --> {end_time}")
        srt_lines.append(text)
        srt_lines.append("")  # Blank line between subtitles

    with open(output_srt, "w", encoding="utf-8") as f:
        f.write("\n".join(srt_lines))
    print(f"Exported SRT to {output_srt}")

def format_timestamp(seconds: float) -> str:
    """
    Converts seconds to SRT timestamp format HH:MM:SS,mmm.
    """
    hours = int(seconds // 3600)
    minutes = int((seconds % 3600) // 60)
    secs = int(seconds % 60)
    milliseconds = int((seconds - int(seconds)) * 1000)
    return f"{hours:02d}:{minutes:02d}:{secs:02d},{milliseconds:03d}"

def main(args):
    input_video = args.input
    output_dir = args.output_dir
    model_size = args.model
    os.makedirs(output_dir, exist_ok=True)

    # Load the Whisper model (GPU will be used if available and configured).
    print(f"Loading Whisper model '{model_size}'...")
    model = whisper.load_model(model_size)
    print(f"Using device: {next(model.parameters()).device}")

    # Determine how many audio tracks exist.
    track_count = get_audio_track_count(input_video)
    print(f"Found {track_count} audio track(s) in '{input_video}'.")

    if track_count == 0:
        print("No audio tracks found. Exiting.")
        sys.exit(1)

    for i in range(track_count):
        # Create a temporary filename for the extracted audio.
        with tempfile.NamedTemporaryFile(suffix=".wav", delete=False) as tmp_file:
            audio_path = tmp_file.name

        try:
            # Extract audio track i.
            extract_audio_track(input_video, i, audio_path)
            # Transcribe extracted audio with Whisper.
            print(f"Transcribing audio track {i}...")
            result = model.transcribe(audio_path)
            # Build output SRT file path.
            srt_out = os.path.join(output_dir, f"audio_track_{i}.srt")
            export_srt(result, srt_out)
        finally:
            # Clean up temporary audio file.
            if os.path.exists(audio_path):
                os.remove(audio_path)

if __name__ == "__main__":
    parser = argparse.ArgumentParser(
        description="Transcribe each audio track in a video to separate SRT files using Whisper."
    )
    parser.add_argument("input", type=str, help="Path to the input video file.")
    parser.add_argument("output_dir", type=str, help="Directory to save the output SRT files.")
    parser.add_argument("--model", type=str, default="base",
                        help="Whisper model to use (tiny, base, small, medium, large). Default is 'base'.")
    args = parser.parse_args()
    main(args)
