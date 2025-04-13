/**
 * FloatingEffect.cs
 *
 * This script applies a floating motion effect to video events by
 * adding random offsets to their VideoMotion keyframes.
 * It works on the selected video events, or if none are selected, it processes all video events.
 *
 * Each keyframe is created based on the event's starting (default) pan/crop state.
 * A random offset is then applied so that the keyframe's bounds are shifted by
 * at most Offset Range pixels along the X and Y axes from that starting state.
 *
 * The script prompts for Keyframe Interval (in seconds) and Offset Range (in pixels)
 * so you can adjust these values without editing the script.
 *
 * Based on documentation section 2.7.
 **/

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using ScriptPortal.Vegas;

public class EntryPoint
{
    public void FromVegas(Vegas vegas)
    {
        // Prompt the user for parameters.
        ParameterForm dlg = new ParameterForm();
        if(dlg.ShowDialog() != DialogResult.OK)
            return; // Abort if the user clicks Cancel

        double keyframeInterval = dlg.KeyframeInterval;  // seconds between keyframes
        double offsetRange = dlg.OffsetRange;            // maximum offset in pixels

        Random random = new Random();
        
        // Build a list of video events to process.
        List<VideoEvent> events = new List<VideoEvent>();
        AddSelectedVideoEvents(vegas, events);
        if (events.Count == 0)
            AddAllVideoEvents(vegas, events);
        
        // Process each video event.
        foreach (VideoEvent videoEvent in events)
        {
            // Clear any existing VideoMotion keyframes.
            videoEvent.VideoMotion.Keyframes.Clear();
            
            // Loop over the duration of the event by time (in seconds)
            double t = 0.0;
            while (Timecode.FromSeconds(t) < videoEvent.Length)
            {
                // Create a new keyframe at time 't' relative to the event start.
                // Because the keyframe is newly created (after clearing),
                // it starts with the event's original pan/crop settings.
                VideoMotionKeyframe keyframe = new VideoMotionKeyframe(Timecode.FromSeconds(t));
                videoEvent.VideoMotion.Keyframes.Add(keyframe);
                
                // Increment time by the defined interval.
                t += keyframeInterval;
            }
            
            // Now apply random offsets to all keyframes,
            // ensuring they are applied relative to the starting (default) state.
            for (int k = 0; k < videoEvent.VideoMotion.Keyframes.Count; k++)
            {
                VideoMotionKeyframe keyframe = videoEvent.VideoMotion.Keyframes[k];

                // Generate random offsets (X and Y) within [-offsetRange, offsetRange].
                double offsetX = (random.NextDouble() * 2 - 1) * offsetRange;
                double offsetY = (random.NextDouble() * 2 - 1) * offsetRange;
                
                // Cast the doubles to float for the VideoMotionVertex constructor.
                VideoMotionVertex offsetVertex = new VideoMotionVertex((float)offsetX, (float)offsetY);
                
                // Apply the random offset using MoveBy.
                keyframe.MoveBy(offsetVertex);
            }
        }
    }
    
    // Adds selected video events (if any) to the provided list.
    void AddSelectedVideoEvents(Vegas vegas, List<VideoEvent> events)
    {
        foreach (Track track in vegas.Project.Tracks)
        {
            if (track.IsVideo())
            {
                foreach (VideoEvent videoEvent in track.Events)
                {
                    if (videoEvent.Selected)
                        events.Add(videoEvent);
                }
            }
        }
    }
    
    // If no video events are selected, adds all video events.
    void AddAllVideoEvents(Vegas vegas, List<VideoEvent> events)
    {
        foreach (Track track in vegas.Project.Tracks)
        {
            if (track.IsVideo())
            {
                foreach (VideoEvent videoEvent in track.Events)
                {
                    events.Add(videoEvent);
                }
            }
        }
    }
}

// --- ParameterForm: a simple Windows Forms dialog for user input ---
public class ParameterForm : Form
{
    private NumericUpDown numInterval;
    private NumericUpDown numOffset;
    private Button okButton;
    private Button cancelButton;

    public ParameterForm()
    {
        // Set up form properties.
        this.Text = "Floating Effect Settings";
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.StartPosition = FormStartPosition.CenterScreen;
        this.ClientSize = new Size(280, 150);
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        
        // Create and add a label and numeric up-down for keyframe interval.
        Label lblInterval = new Label();
        lblInterval.Text = "Keyframe Interval (sec):";
        lblInterval.Location = new Point(10, 10);
        lblInterval.AutoSize = true;
        this.Controls.Add(lblInterval);

        numInterval = new NumericUpDown();
        numInterval.DecimalPlaces = 2;
        numInterval.Minimum = 0.1M;
        numInterval.Maximum = 10;
        numInterval.Value = 0.5M; // default value
        numInterval.Increment = 0.1M;
        numInterval.Location = new Point(10, 30);
        numInterval.Width = 100;
        this.Controls.Add(numInterval);
        
        // Create and add a label and numeric up-down for offset range.
        Label lblOffset = new Label();
        lblOffset.Text = "Offset Range (pixels):";
        lblOffset.Location = new Point(10, 60);
        lblOffset.AutoSize = true;
        this.Controls.Add(lblOffset);

        numOffset = new NumericUpDown();
        numOffset.DecimalPlaces = 1;
        numOffset.Minimum = 1;
        numOffset.Maximum = 100;
        numOffset.Value = 5;
        numOffset.Increment = 1;
        numOffset.Location = new Point(10, 80);
        numOffset.Width = 100;
        this.Controls.Add(numOffset);
        
        // Create and add OK and Cancel buttons.
        okButton = new Button();
        okButton.Text = "OK";
        okButton.DialogResult = DialogResult.OK;
        okButton.Location = new Point(40, 110);
        this.Controls.Add(okButton);
        this.AcceptButton = okButton;

        cancelButton = new Button();
        cancelButton.Text = "Cancel";
        cancelButton.DialogResult = DialogResult.Cancel;
        cancelButton.Location = new Point(140, 110);
        this.Controls.Add(cancelButton);
        this.CancelButton = cancelButton;
    }
    
    public double KeyframeInterval
    {
        get { return (double)numInterval.Value; }
    }
    
    public double OffsetRange
    {
        get { return (double)numOffset.Value; }
    }
}
