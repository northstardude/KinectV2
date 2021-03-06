﻿// --------------------------------------------------------------------------------------------------------------------
// <copyright file="MainWindow.xaml.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

namespace Microsoft.Samples.Kinect.HDFaceBasics
{
    using System;
    using System.ComponentModel;
    using System.Windows;
    using System.Windows.Media;
    using System.Windows.Media.Media3D;
    using Microsoft.Kinect;
    using Microsoft.Kinect.Face;
    using Bespoke.Common.Osc;
    using System.Net;
    using System.Collections.Generic;


    /// <summary>
    /// Main Window
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged, IDisposable
    {
        /// <summary>
        /// Currently used KinectSensor
        /// </summary>
        private KinectSensor sensor = null;

        /// <summary>
        /// Body frame source to get a BodyFrameReader
        /// </summary>
        private BodyFrameSource bodySource = null;

        /// <summary>
        /// Body frame reader to get body frames
        /// </summary>
        private BodyFrameReader bodyReader = null;

        /// <summary>
        /// HighDefinitionFaceFrameSource to get a reader and a builder from.
        /// Also to set the currently tracked user id to get High Definition Face Frames of
        /// </summary>
        private HighDefinitionFaceFrameSource highDefinitionFaceFrameSource = null;

        /// <summary>
        /// HighDefinitionFaceFrameReader to read HighDefinitionFaceFrame to get FaceAlignment
        /// </summary>
        private HighDefinitionFaceFrameReader highDefinitionFaceFrameReader = null;

        /// <summary>
        /// FaceAlignment is the result of tracking a face, it has face animations location and orientation
        /// </summary>
        private FaceAlignment currentFaceAlignment = null;

        /// <summary>
        /// FaceModel is a result of capturing a face
        /// </summary>
        private FaceModel currentFaceModel = null;

        /// <summary>
        /// FaceModelBuilder is used to produce a FaceModel
        /// </summary>
        private FaceModelBuilder faceModelBuilder = null;

        /// <summary>
        /// The currently tracked body
        /// </summary>
        private Body currentTrackedBody = null;

        /// <summary>
        /// The currently tracked body
        /// </summary>
        private ulong currentTrackingId = 0;

        /// <summary>
        /// Gets or sets the current tracked user id
        /// </summary>
        private string currentBuilderStatus = string.Empty;

        /// <summary>
        /// Gets or sets the current status text to display
        /// </summary>
        private string statusText = "Ready To Start Capture";

        /// <summary>
        /// These are for entering the keyboard commands
        /// </summary>
        IPEndPoint glovepie;
        IPEndPoint myapp;
        OscMessage msg;

        /// <summary>
        /// THese are float variabls for the various AUs
        /// </summary>
        float jawopen;
        float leftLipPull;
        float leftCheekPuff;
        float rightCheekPuff;
        float rightLipPull;
        float brow;

        /// <summary>
        /// Sets the command to send to the glovepie sketch
        /// </summary>
        int commandToSend;

        /// <summary>
        /// is true if the device is in training mdoe
        /// </summary>
        bool trainingmode = true;

        /// <summary>
        /// keeps track of how many data points you have logged during training
        /// </summary>
        int logCounter = 1;

        /// <summary>
        /// Stores the result of the matlab neural network as a ong string
        /// </summary>
        string result;

        /// <summary>
        /// A list containing only numerical strings from the matlab nn result
        /// </summary>
        List<String> numbersOnly;

        /// <summary>
        /// The matrix containing the input data
        /// </summary>
        float[] inputarray;

        /// <summary>
        /// Used to determine which command should be selected
        /// </summary>
        int commandCounter = 0;

        /// <summary>
        /// The instance of Matlab used in this code
        /// </summary>
        MLApp.MLApp matlab;

        /// <summary>
        /// Value used for identifying patient's max head rotating ability for command ID 3
        /// </summary>
        double maxHeadRot = 0.0;

        /// <summary>
        /// Initializes a new instance of the MainWindow class.
        /// </summary>
        public MainWindow()
        {
            this.InitializeComponent();
            this.DataContext = this;

            //establish protocol for using glovepie to enter commands
            OscPacket.LittleEndianByteOrder = false;
            myapp = new IPEndPoint(IPAddress.Loopback, 1944);
            glovepie = new IPEndPoint(IPAddress.Loopback, 1945);

        }

        /// <summary>
        /// INotifyPropertyChangedPropertyChanged event to allow window controls to bind to changeable data
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Gets or sets the current status text to display
        /// </summary>
        public string StatusText
        {
            get
            {
                return this.statusText;
            }

            set
            {
                if (this.statusText != value)
                {
                    this.statusText = value;

                    // notify any bound elements that the text has changed
                    if (this.PropertyChanged != null)
                    {
                        this.PropertyChanged(this, new PropertyChangedEventArgs("StatusText"));
                    }
                }
            }
        }

        /// <summary>
        /// Gets or sets the current tracked user id
        /// </summary>
        private ulong CurrentTrackingId
        {
            get
            {
                return this.currentTrackingId;
            }

            set
            {
                this.currentTrackingId = value;

                this.StatusText = this.MakeStatusText();
            }
        }

        /// <summary>
        /// Gets or sets the current Face Builder instructions to user
        /// </summary>
        private string CurrentBuilderStatus
        {
            get
            {
                return this.currentBuilderStatus;
            }

            set
            {
                this.currentBuilderStatus = value;

                this.StatusText = this.MakeStatusText();
            }
        }

        /// <summary>
        /// Called when disposed of
        /// </summary>
        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Dispose based on whether or not managed or native resources should be freed
        /// </summary>
        /// <param name="disposing">Set to true to free both native and managed resources, false otherwise</param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (this.currentFaceModel != null)
                {
                    this.currentFaceModel.Dispose();
                    this.currentFaceModel = null;
                }
            }
        }

        /// <summary>
        /// Returns the length of a vector from origin
        /// </summary>
        /// <param name="point">Point in space to find it's distance from origin</param>
        /// <returns>Distance from origin</returns>
        private static double VectorLength(CameraSpacePoint point)
        {
            var result = Math.Pow(point.X, 2) + Math.Pow(point.Y, 2) + Math.Pow(point.Z, 2);

            result = Math.Sqrt(result);

            return result;
        }

        /// <summary>
        /// Finds the closest body from the sensor if any
        /// </summary>
        /// <param name="bodyFrame">A body frame</param>
        /// <returns>Closest body, null of none</returns>
        private static Body FindClosestBody(BodyFrame bodyFrame)
        {
            Body result = null;
            double closestBodyDistance = double.MaxValue;

            Body[] bodies = new Body[bodyFrame.BodyCount];
            bodyFrame.GetAndRefreshBodyData(bodies);

            foreach (var body in bodies)
            {
                if (body.IsTracked)
                {
                    var currentLocation = body.Joints[JointType.SpineBase].Position;

                    var currentDistance = VectorLength(currentLocation);

                    if (result == null || currentDistance < closestBodyDistance)
                    {
                        result = body;
                        closestBodyDistance = currentDistance;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Find if there is a body tracked with the given trackingId
        /// </summary>
        /// <param name="bodyFrame">A body frame</param>
        /// <param name="trackingId">The tracking Id</param>
        /// <returns>The body object, null of none</returns>
        private static Body FindBodyWithTrackingId(BodyFrame bodyFrame, ulong trackingId)
        {
            Body result = null;

            Body[] bodies = new Body[bodyFrame.BodyCount];
            bodyFrame.GetAndRefreshBodyData(bodies);

            foreach (var body in bodies)
            {
                if (body.IsTracked)
                {
                    if (body.TrackingId == trackingId)
                    {
                        result = body;
                        break;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Gets the current collection status
        /// </summary>
        /// <param name="status">Status value</param>
        /// <returns>Status value as text</returns>
        private static string GetCollectionStatusText(FaceModelBuilderCollectionStatus status)
        {
            string res = string.Empty;

            if ((status & FaceModelBuilderCollectionStatus.FrontViewFramesNeeded) != 0)
            {
                res = "FrontViewFramesNeeded";
                return res;
            }

            if ((status & FaceModelBuilderCollectionStatus.LeftViewsNeeded) != 0)
            {
                res = "LeftViewsNeeded";
                return res;
            }

            if ((status & FaceModelBuilderCollectionStatus.RightViewsNeeded) != 0)
            {
                res = "RightViewsNeeded";
                return res;
            }

            if ((status & FaceModelBuilderCollectionStatus.TiltedUpViewsNeeded) != 0)
            {
                res = "TiltedUpViewsNeeded";
                return res;
            }

            if ((status & FaceModelBuilderCollectionStatus.Complete) != 0)
            {
                res = "Complete";
                return res;
            }

            if ((status & FaceModelBuilderCollectionStatus.MoreFramesNeeded) != 0)
            {
                res = "TiltedUpViewsNeeded";
                return res;
            }

            return res;
        }

        /// <summary>
        /// Helper function to format a status message
        /// </summary>
        /// <returns>Status text</returns>
        private string MakeStatusText()
        {
            string status = string.Format(System.Globalization.CultureInfo.CurrentCulture, "Builder Status: {0}, Current Tracking ID: {1}", this.CurrentBuilderStatus, this.CurrentTrackingId);

            return status;
        }

        /// <summary>
        /// Fires when Window is Loaded
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            this.InitializeHDFace();
        }

        /// <summary>
        /// Initialize Kinect object
        /// </summary>
        private void InitializeHDFace()
        {
            this.CurrentBuilderStatus = "Ready To Start Capture";

            this.sensor = KinectSensor.GetDefault();
            this.bodySource = this.sensor.BodyFrameSource;
            this.bodyReader = this.bodySource.OpenReader();
            this.bodyReader.FrameArrived += this.BodyReader_FrameArrived;

            this.highDefinitionFaceFrameSource = new HighDefinitionFaceFrameSource(this.sensor);
            this.highDefinitionFaceFrameSource.TrackingIdLost += this.HdFaceSource_TrackingIdLost;

            this.highDefinitionFaceFrameReader = this.highDefinitionFaceFrameSource.OpenReader();
            this.highDefinitionFaceFrameReader.FrameArrived += this.HdFaceReader_FrameArrived;

            this.currentFaceModel = new FaceModel();
            this.currentFaceAlignment = new FaceAlignment();

            this.InitializeMesh();
            this.UpdateMesh();

            this.sensor.Open();
        }

        /// <summary>
        /// Initializes a 3D mesh to deform every frame
        /// </summary>
        private void InitializeMesh()
        {
            var vertices = this.currentFaceModel.CalculateVerticesForAlignment(this.currentFaceAlignment);

            var triangleIndices = this.currentFaceModel.TriangleIndices;

            var indices = new Int32Collection(triangleIndices.Count);

            for (int i = 0; i < triangleIndices.Count; i += 3)
            {
                uint index01 = triangleIndices[i];
                uint index02 = triangleIndices[i + 1];
                uint index03 = triangleIndices[i + 2];

                indices.Add((int)index03);
                indices.Add((int)index02);
                indices.Add((int)index01);
            }

            this.theGeometry.TriangleIndices = indices;
            this.theGeometry.Normals = null;
            this.theGeometry.Positions = new Point3DCollection();
            this.theGeometry.TextureCoordinates = new PointCollection();

            foreach (var vert in vertices)
            {
                this.theGeometry.Positions.Add(new Point3D(vert.X, vert.Y, -vert.Z));
                this.theGeometry.TextureCoordinates.Add(new Point());
            }
        }

        /// <summary>
        /// Sends the new deformed mesh to be drawn
        /// </summary>
        private void UpdateMesh()
        {
            var vertices = this.currentFaceModel.CalculateVerticesForAlignment(this.currentFaceAlignment);

            for (int i = 0; i < vertices.Count; i++)
            {
                var vert = vertices[i];
                this.theGeometry.Positions[i] = new Point3D(vert.X, vert.Y, -vert.Z);
            }

            //set the AU float with the updated values
            jawopen = this.currentFaceAlignment.AnimationUnits[FaceShapeAnimations.JawOpen];
            leftCheekPuff = this.currentFaceAlignment.AnimationUnits[FaceShapeAnimations.LeftcheekPuff];
            rightCheekPuff = this.currentFaceAlignment.AnimationUnits[FaceShapeAnimations.RightcheekPuff];
            leftLipPull = this.currentFaceAlignment.AnimationUnits[FaceShapeAnimations.LipCornerPullerLeft];
            rightLipPull = this.currentFaceAlignment.AnimationUnits[FaceShapeAnimations.LipCornerPullerRight];
            brow = this.currentFaceAlignment.AnimationUnits[FaceShapeAnimations.LefteyebrowLowerer];

            //set training mode or input mode
            if (training.IsChecked == true)
            {
                trainingmode = true;
               // Status.Text = "Training mode ON";
            }
            else
            {
                trainingmode = false;
                Status.Text = "Training mode OFF";
            }
            if (trainingmode == true)
            {
                if (this.currentFaceAlignment.FaceOrientation.X > maxHeadRot)
                {
                    maxHeadRot = this.currentFaceAlignment.FaceOrientation.X;
                }
                Status.Text = "Current: " + (Math.Round(this.currentFaceAlignment.FaceOrientation.X, 3)).ToString() + "\nMax: "+(Math.Round(maxHeadRot, 3)).ToString();
            }

            if (trainingmode == false)
                {

                    //Initialize lists for output amd input
                    numbersOnly = new List<string>();
                    inputarray = new float[6];

                    //fill the input matrix
                    inputarray[0] = jawopen;
                    inputarray[1] = leftCheekPuff;
                    inputarray[2] = rightCheekPuff;
                    inputarray[3] = leftLipPull;
                    inputarray[4] = rightLipPull;
                    inputarray[5] = brow;

                    //initialize matlab
                    matlab = new MLApp.MLApp();
                    //Status.Text = ("Matlab ON");
                    //Put data into the matlab workspace
                    matlab.PutWorkspaceData("input", "base", inputarray);


                    //execute the function by first transposing the input matrix and then executing the nn. Store the data in a string
                    //below is a series of nn "profiles" uncomment the correct one
                    //result = (matlab.Execute("kinectv2NN(transpose(input))"));
                    result = (matlab.Execute("balaNN(transpose(input))"));

                    //go through the 'result' string and split it into different words. Remove all blank spaces. 
                    char[] delimiters = new char[] { };
                    string[] parts = result.Split(delimiters,
                             StringSplitOptions.RemoveEmptyEntries);

                    //go through each word from the 'parts' list and extract the numbers
                    for (int i = 0; i < parts.Length; i++)
                    {
                        if (parts[i].Equals("ans")) { } //ignore words if it equals 'ans'
                        else if (parts[i].Equals("=")) { } //ignore words that are '='
                        else { numbersOnly.Add(parts[i]); } //add the word to a new list if it is a number
                    }

                    //dtermine which command to send
                    foreach (string str in numbersOnly)
                    {
                        float x = float.Parse(str);
                        x += 0.5f;
                        if ((int)x == 0)
                        {
                            commandCounter++; //don't send this command
                        }
                        else
                        {
                            commandToSend = commandCounter;

                            if (commandToSend == 3)
                            {
                                commandToSend = 0;
                            }
                            if (this.currentFaceAlignment.FaceOrientation.X > float.Parse(HeadRotationValue.Text.ToString()))
                            {
                                commandToSend = 3;
                            }
                            sendCommand(commandToSend);
                            Status.Text = ("Input: " + commandToSend); //Later I'll put in the send command, for now just show it in the GUI

                        }
                    }
                    commandCounter = 0; //reset command counter for next run

                }
        }

        /// <summary>
        /// Start a face capture on clicking the button
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private void StartCapture_Button_Click(object sender, RoutedEventArgs e)
        {
            this.StartCapture();
        }

        /// <summary>
        /// This event fires when a BodyFrame is ready for consumption
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private void BodyReader_FrameArrived(object sender, BodyFrameArrivedEventArgs e)
        {
            this.CheckOnBuilderStatus();

            var frameReference = e.FrameReference;
            using (var frame = frameReference.AcquireFrame())
            {
                if (frame == null)
                {
                    // We might miss the chance to acquire the frame, it will be null if it's missed
                    return;
                }

                if (this.currentTrackedBody != null)
                {
                    this.currentTrackedBody = FindBodyWithTrackingId(frame, this.CurrentTrackingId);

                    if (this.currentTrackedBody != null)
                    {
                        return;
                    }
                }

                Body selectedBody = FindClosestBody(frame);

                if (selectedBody == null)
                {
                    return;
                }

                this.currentTrackedBody = selectedBody;
                this.CurrentTrackingId = selectedBody.TrackingId;

                this.highDefinitionFaceFrameSource.TrackingId = this.CurrentTrackingId;
            }
        }

        /// <summary>
        /// This event is fired when a tracking is lost for a body tracked by HDFace Tracker
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private void HdFaceSource_TrackingIdLost(object sender, TrackingIdLostEventArgs e)
        {
            var lostTrackingID = e.TrackingId;

            if (this.CurrentTrackingId == lostTrackingID)
            {
                this.CurrentTrackingId = 0;
                this.currentTrackedBody = null;
                if (this.faceModelBuilder != null)
                {
                    this.faceModelBuilder.Dispose();
                    this.faceModelBuilder = null;
                }

                this.highDefinitionFaceFrameSource.TrackingId = 0;
            }
        }

        /// <summary>
        /// This event is fired when a new HDFace frame is ready for consumption
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private void HdFaceReader_FrameArrived(object sender, HighDefinitionFaceFrameArrivedEventArgs e)
        {
            using (var frame = e.FrameReference.AcquireFrame())
            {
                // We might miss the chance to acquire the frame; it will be null if it's missed.
                // Also ignore this frame if face tracking failed.
                if (frame == null || !frame.IsFaceTracked)
                {
                    return;
                }

                frame.GetAndRefreshFaceAlignmentResult(this.currentFaceAlignment);
                this.UpdateMesh();
            }
        }

        /// <summary>
        /// Start a face capture operation
        /// </summary>
        private void StartCapture()
        {
            this.StopFaceCapture();

            this.faceModelBuilder = null;

            this.faceModelBuilder = this.highDefinitionFaceFrameSource.OpenModelBuilder(FaceModelBuilderAttributes.None);

            this.faceModelBuilder.BeginFaceDataCollection();

            this.faceModelBuilder.CollectionCompleted += this.HdFaceBuilder_CollectionCompleted;
        }

        /// <summary>
        /// Cancel the current face capture operation
        /// </summary>
        private void StopFaceCapture()
        {
            if (this.faceModelBuilder != null)
            {
                this.faceModelBuilder.Dispose();
                this.faceModelBuilder = null;
            }
        }

        /// <summary>
        /// This event fires when the face capture operation is completed
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private void HdFaceBuilder_CollectionCompleted(object sender, FaceModelBuilderCollectionCompletedEventArgs e)
        {
            var modelData = e.ModelData;

            this.currentFaceModel = modelData.ProduceFaceModel();

            this.faceModelBuilder.Dispose();
            this.faceModelBuilder = null;

            this.CurrentBuilderStatus = "Capture Complete";
        }

        /// <summary>
        /// Check the face model builder status
        /// </summary>
        private void CheckOnBuilderStatus()
        {
            if (this.faceModelBuilder == null)
            {
                return;
            }

            string newStatus = string.Empty;

            var captureStatus = this.faceModelBuilder.CaptureStatus;
            newStatus += captureStatus.ToString();

            var collectionStatus = this.faceModelBuilder.CollectionStatus;

            newStatus += ", " + GetCollectionStatusText(collectionStatus);

            this.CurrentBuilderStatus = newStatus;
        }

        public void sendCommand(int commandtoSend)
        {
            switch (commandtoSend)
            {
                case 0:
                    msg = new OscMessage(myapp, "/move/w", 0.0f);
                    msg.Send(glovepie);
                    msg = new OscMessage(myapp, "/move/a", 0.0f);
                    msg.Send(glovepie);
                    msg = new OscMessage(myapp, "/move/s", 0.0f);
                    msg.Send(glovepie);
                    msg = new OscMessage(myapp, "/move/d", 0.0f);
                    msg.Send(glovepie);
                    msg = new OscMessage(myapp, "/move/lc", 0.0f);
                    msg.Send(glovepie);
                    msg = new OscMessage(myapp, "/move/rc", 0.0f);
                    msg.Send(glovepie);
                    msg = new OscMessage(myapp, "/move/middle", 0.0f);
                    msg.Send(glovepie);
                    msg = new OscMessage(myapp, "/move/space", 0.0f);
                    msg.Send(glovepie);
                    break;
                case 1:
                    msg = new OscMessage(myapp, "/move/w", 10.0f);
                    msg.Send(glovepie);
                    msg = new OscMessage(myapp, "/move/a", 0.0f);
                    msg.Send(glovepie);
                    msg = new OscMessage(myapp, "/move/s", 0.0f);
                    msg.Send(glovepie);
                    msg = new OscMessage(myapp, "/move/d", 0.0f);
                    msg.Send(glovepie);
                    msg = new OscMessage(myapp, "/move/lc", 0.0f);
                    msg.Send(glovepie);
                    msg = new OscMessage(myapp, "/move/rc", 0.0f);
                    msg.Send(glovepie);
                    msg = new OscMessage(myapp, "/move/space", 0.0f);
                    msg.Send(glovepie);
                    msg = new OscMessage(myapp, "/move/middle", 0.0f);
                    msg.Send(glovepie);
                    break;
                case 2:
                    msg = new OscMessage(myapp, "/move/w", 0.0f);
                    msg.Send(glovepie);
                    msg = new OscMessage(myapp, "/move/a", 10.0f);
                    msg.Send(glovepie);
                    msg = new OscMessage(myapp, "/move/s", 0.0f);
                    msg.Send(glovepie);
                    msg = new OscMessage(myapp, "/move/d", 0.0f);
                    msg.Send(glovepie);
                    msg = new OscMessage(myapp, "/move/lc", 0.0f);
                    msg.Send(glovepie);
                    msg = new OscMessage(myapp, "/move/rc", 0.0f);
                    msg.Send(glovepie);
                    msg = new OscMessage(myapp, "/move/space", 0.0f);
                    msg.Send(glovepie);
                    msg = new OscMessage(myapp, "/move/middle", 0.0f);
                    msg.Send(glovepie);
                    break;
                case 3:
                    msg = new OscMessage(myapp, "/move/w", 0.0f);
                    msg.Send(glovepie);
                    msg = new OscMessage(myapp, "/move/a", 0.0f);
                    msg.Send(glovepie);
                    msg = new OscMessage(myapp, "/move/s", 10.0f);
                    msg.Send(glovepie);
                    msg = new OscMessage(myapp, "/move/d", 0.0f);
                    msg.Send(glovepie);
                    msg = new OscMessage(myapp, "/move/lc", 0.0f);
                    msg.Send(glovepie);
                    msg = new OscMessage(myapp, "/move/rc", 0.0f);
                    msg.Send(glovepie);
                    msg = new OscMessage(myapp, "/move/space", 0.0f);
                    msg.Send(glovepie);
                    msg = new OscMessage(myapp, "/move/middle", 0.0f);
                    msg.Send(glovepie);
                    break;
                case 4:
                    msg = new OscMessage(myapp, "/move/w", 0.0f);
                    msg.Send(glovepie);
                    msg = new OscMessage(myapp, "/move/a", 0.0f);
                    msg.Send(glovepie);
                    msg = new OscMessage(myapp, "/move/s", 0.0f);
                    msg.Send(glovepie);
                    msg = new OscMessage(myapp, "/move/d", 10.0f);
                    msg.Send(glovepie);
                    msg = new OscMessage(myapp, "/move/lc", 0.0f);
                    msg.Send(glovepie);
                    msg = new OscMessage(myapp, "/move/rc", 0.0f);
                    msg.Send(glovepie);
                    msg = new OscMessage(myapp, "/move/space", 0.0f);
                    msg.Send(glovepie);
                    msg = new OscMessage(myapp, "/move/middle", 0.0f);
                    msg.Send(glovepie);
                    break;
                case 5:
                    msg = new OscMessage(myapp, "/move/w", 0.0f);
                    msg.Send(glovepie);
                    msg = new OscMessage(myapp, "/move/a", 0.0f);
                    msg.Send(glovepie);
                    msg = new OscMessage(myapp, "/move/s", 0.0f);
                    msg.Send(glovepie);
                    msg = new OscMessage(myapp, "/move/d", 0.0f);
                    msg.Send(glovepie);
                    msg = new OscMessage(myapp, "/move/lc", 10.0f);
                    msg.Send(glovepie);
                    msg = new OscMessage(myapp, "/move/rc", 0.0f);
                    msg.Send(glovepie);
                    msg = new OscMessage(myapp, "/move/space", 0.0f);
                    msg.Send(glovepie);
                    msg = new OscMessage(myapp, "/move/middle", 0.0f);
                    msg.Send(glovepie);
                    break;
                case 6:
                    msg = new OscMessage(myapp, "/move/w", 0.0f);
                    msg.Send(glovepie);
                    msg = new OscMessage(myapp, "/move/a", 0.0f);
                    msg.Send(glovepie);
                    msg = new OscMessage(myapp, "/move/s", 0.0f);
                    msg.Send(glovepie);
                    msg = new OscMessage(myapp, "/move/d", 0.0f);
                    msg.Send(glovepie);
                    msg = new OscMessage(myapp, "/move/lc", 0.0f);
                    msg.Send(glovepie);
                    msg = new OscMessage(myapp, "/move/rc", 10.0f);
                    msg.Send(glovepie);
                    msg = new OscMessage(myapp, "/move/space", 0.0f);
                    msg.Send(glovepie);
                    msg = new OscMessage(myapp, "/move/middle", 0.0f);
                    msg.Send(glovepie);
                    break;
                case 7:
                    msg = new OscMessage(myapp, "/move/w", 10.0f);
                    msg.Send(glovepie);
                    msg = new OscMessage(myapp, "/move/a", 0.0f);
                    msg.Send(glovepie);
                    msg = new OscMessage(myapp, "/move/s", 0.0f);
                    msg.Send(glovepie);
                    msg = new OscMessage(myapp, "/move/d", 0.0f);
                    msg.Send(glovepie);
                    msg = new OscMessage(myapp, "/move/lc", 0.0f);
                    msg.Send(glovepie);
                    msg = new OscMessage(myapp, "/move/rc", 0.0f);
                    msg.Send(glovepie);
                    msg = new OscMessage(myapp, "/move/space", 10.0f);
                    msg.Send(glovepie);
                    msg = new OscMessage(myapp, "/move/middle", 0.0f);
                    msg.Send(glovepie);
                    break;
                case 8:
                    msg = new OscMessage(myapp, "/move/w", 0.0f);
                    msg.Send(glovepie);
                    msg = new OscMessage(myapp, "/move/a", 0.0f);
                    msg.Send(glovepie);
                    msg = new OscMessage(myapp, "/move/s", 0.0f);
                    msg.Send(glovepie);
                    msg = new OscMessage(myapp, "/move/d", 0.0f);
                    msg.Send(glovepie);
                    msg = new OscMessage(myapp, "/move/lc", 0.0f);
                    msg.Send(glovepie);
                    msg = new OscMessage(myapp, "/move/rc", 0.0f);
                    msg.Send(glovepie);
                    msg = new OscMessage(myapp, "/move/space", 0.0f);
                    msg.Send(glovepie);
                    msg = new OscMessage(myapp, "/move/middle", 10.0f);
                    msg.Send(glovepie);
                    break;
            }
        }

        private void logData(object sender, RoutedEventArgs e)
        {
            //if training mode, we want to write the data to a file
            if (trainingmode == true)
            {
                using (System.IO.StreamWriter file = new System.IO.StreamWriter(@"C:\Users\Bala\Desktop\Sophomore\Science Fair\Development and Optimization\Kinect V2\Inputs.txt", true))
                {
                    file.WriteLine(jawopen + ", " + leftCheekPuff + ", " + rightCheekPuff + ", " + leftLipPull + ", " + rightLipPull + ", " + brow);
                    Status.Text = ("Total Points logged: " + logCounter);
                    logCounter++;

                }
                using (System.IO.StreamWriter file = new System.IO.StreamWriter(@"C:\Users\Bala\Desktop\Sophomore\Science Fair\Development and Optimization\Kinect V2\Targets.txt", true))
                {
                    switch (Int32.Parse(Input.Text.ToString()))
                    {
                        case 0:
                            file.WriteLine("1 0 0 0 0 0 0 0 0");
                            break;
                        case 1:
                            file.WriteLine("0 1 0 0 0 0 0 0 0");
                            break;
                        case 2:
                            file.WriteLine("0 0 1 0 0 0 0 0 0");
                            break;
                        case 3:
                            file.WriteLine("0 0 0 1 0 0 0 0 0");
                            break;
                        case 4:
                            file.WriteLine("0 0 0 0 1 0 0 0 0");
                            break;
                        case 5:
                            file.WriteLine("0 0 0 0 0 1 0 0 0");
                            break;
                        case 6:
                            file.WriteLine("0 0 0 0 0 0 1 0 0");
                            break;
                        case 7:
                            file.WriteLine("0 0 0 0 0 0 0 1 0");
                            break;
                        case 8:
                            file.WriteLine("0 0 0 0 0 0 0 0 1");
                            break;
                    }
                }
            }
        }
    }
}