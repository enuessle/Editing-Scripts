/**
 * Soft Shake.cs
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
        VideoKeyframeType selectedType = dlg.SelectedKeyframeType; // user-selected keyframe type
        float smoothness = dlg.Smoothness;               // keyframe smoothness value

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
            // Ensuring they are applied relative to the starting (default) state.
            for (int k = 0; k < videoEvent.VideoMotion.Keyframes.Count; k++)
            {
                VideoMotionKeyframe keyframe = videoEvent.VideoMotion.Keyframes[k];

                // Set Keyframe Type
                keyframe.Type = selectedType;
                // Set Keyframe Smoothness
                keyframe.Smoothness = smoothness;

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

// --- ParameterForm: A Windows Forms dialog for user input ---
public class ParameterForm : Form
{
    private NumericUpDown numInterval;
    private NumericUpDown numOffset;
    private ComboBox cmbKeyframeType;
    private NumericUpDown numSmooth;
    private Button okButton;
    private Button cancelButton;
    
    public ParameterForm()
    {
        // Form settings.
        this.Text = "Floating Effect Settings";
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.StartPosition = FormStartPosition.CenterScreen;
        this.ClientSize = new Size(340, 240);
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        
        // Keyframe Interval label and control.
        Label lblInterval = new Label();
        lblInterval.Text = "Keyframe Interval (sec):";
        lblInterval.Location = new Point(10, 10);
        lblInterval.AutoSize = true;
        this.Controls.Add(lblInterval);

        numInterval = new NumericUpDown();
        numInterval.DecimalPlaces = 2;
        numInterval.Minimum = 0.1M;
        numInterval.Maximum = 10;
        numInterval.Value = 0.4M;
        numInterval.Increment = 0.1M;
        numInterval.Location = new Point(10, 30);
        numInterval.Width = 100;
        this.Controls.Add(numInterval);
        
        // Offset Range label and control.
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

        // Keyframe Type label and ComboBox.
        Label lblType = new Label();
        lblType.Text = "Keyframe Type:";
        lblType.Location = new Point(10, 110);
        lblType.AutoSize = true;
        this.Controls.Add(lblType);
        
        cmbKeyframeType = new ComboBox();
        cmbKeyframeType.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbKeyframeType.Location = new Point(10, 130);
        cmbKeyframeType.Width = 150;
        cmbKeyframeType.Items.Add("Linear");
        cmbKeyframeType.Items.Add("Hold");
        cmbKeyframeType.Items.Add("Slow");
        cmbKeyframeType.Items.Add("Fast");
        cmbKeyframeType.Items.Add("Smooth");
        cmbKeyframeType.Items.Add("Sharp");
        cmbKeyframeType.SelectedIndex = 0; // default selection
        this.Controls.Add(cmbKeyframeType);
        
        // Smoothness label and control.
        Label lblSmooth = new Label();
        lblSmooth.Text = "Smoothness (0.0 - 1.0):";
        lblSmooth.Location = new Point(10, 160);
        lblSmooth.AutoSize = true;
        this.Controls.Add(lblSmooth);

        numSmooth = new NumericUpDown();
        numSmooth.DecimalPlaces = 2;
        numSmooth.Minimum = 0.0M;
        numSmooth.Maximum = 1.0M;
        numSmooth.Value = 1.0M;
        numSmooth.Increment = 0.05M;
        numSmooth.Location = new Point(10, 180);
        numSmooth.Width = 100;
        this.Controls.Add(numSmooth);
        
        // OK and Cancel buttons.
        okButton = new Button();
        okButton.Text = "OK";
        okButton.DialogResult = DialogResult.OK;
        okButton.Location = new Point(60, 210);
        this.Controls.Add(okButton);
        this.AcceptButton = okButton;

        cancelButton = new Button();
        cancelButton.Text = "Cancel";
        cancelButton.DialogResult = DialogResult.Cancel;
        cancelButton.Location = new Point(180, 210);
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
    
    public VideoKeyframeType SelectedKeyframeType
    {
        get 
        {
            // Convert the selected string to a VideoKeyframeType enum value.
            return (VideoKeyframeType)Enum.Parse(typeof(VideoKeyframeType), cmbKeyframeType.SelectedItem.ToString());
        }
    }

    public float Smoothness
    {
        get { return (float)numSmooth.Value; }
    }
}