﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AForge.Math;
using Android.App;
using Android.Content;
using Android.Media;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using Java.IO;
using SciChartHeatmapAudio.Helpers;
using static SciChartHeatmapAudio.Helpers.WvlLogger;

namespace SciChartHeatmapAudio.Services
{
    public class WavAudioService
    {
        // default
        
        public event EventHandler samplesUpdated;

        // AudiRecord
        AudioRecord audioRecord;
        AudioRecord audioRecordCharts;
        //CloneableAudioRecord audioRecord;
        //CloneableAudioRecord audioRecordCharts;
        private static int RECORDER_BPP = 16;
        private static int RECORDER_SAMPLERATE = 44100;
        //private static int RECORDER_CHANNELS = AudioFormat.CHANNEL_IN_STEREO;        
        //private static int RECORDER_CHANNELS = 12;
        //private static int RECORDER_CHANNELS = AudioFormat.CHANNEL_IN_MONO;        
        private static int RECORDER_CHANNELS = 16;
        //private static int RECORDER_AUDIO_ENCODING = AudioFormat.Enc ENCODING_PCM_16BIT;
        private static int RECORDER_AUDIO_ENCODING = 2;

        // Bools
        bool isRecording = false;
        bool isWriting = false;

        // Files
        private static string AUDIO_RECORDER_FILE_EXT_WAV = ".wav";
        private static string AUDIO_RECORDER_FOLDER = "AudioRecorder";
        private static string AUDIO_RECORDER_TEMP_FILE = "record_temp.raw";
        private string wavFileName;


        private System.Threading.Thread recordingThread = null;
        private System.Threading.Thread chartsThread = null;
        private Thread samplesUpdatedThread = null;

        //int bufferSize = 2048 * sizeof(byte);
        int bufferSize = 1024 * sizeof(byte);
        byte[] buffer;

        int cumulBufferSize = 0;
        int bufferCount = 0;
        byte[] cumulBufferByte;
        short[] cumulBufferShort;
        int[] cumulBufferInt;

        AudioTrack audioTrack = null;

        #region FFT

        //public int[] FFT(int[] y)
        public async Task<int[]> FFT(int[] y)
        {
            WvlLogger.Log(LogType.TraceAll,"FFT()");
            var input = new AForge.Math.Complex[y.Length];

            for (int i = 0; i < y.Length; i++)
            {
                input[i] = new AForge.Math.Complex(y[i], 0);
            }

            FourierTransform.FFT(input, FourierTransform.Direction.Forward);

            var result = new int[y.Length / 2];

            // getting magnitude
            for (int i = 0; i < y.Length / 2 - 1; i++)
            {
                var current = Math.Sqrt(input[i].Re * input[i].Re + input[i].Im * input[i].Im);
                current = Math.Log10(current) * 10;
                result[i] = (int)current;
            }

            samplesUpdated(this, new SamplesUpdatedEventArgs(result));
            //PrepareHeatmapDataSeries(result);
            return result;
        }

        public void PrepareHeatmapDataSeries(int[] data)
        {
            WvlLogger.Log(LogType.TraceAll, "PrepareHeatmapDataSeries()");
            int width = 512;
            int height = 512;
            int[] Data = new int[width * height];

            //WvlLogger.Log(LogType.TraceAll,"UpdateHeatmapDataSeries - Width : " + width.ToString() + " - Height : " + height.ToString());
            WvlLogger.Log(LogType.TraceValues, "PrepareHeatmapDataSeries() - Data before Array.Copy() : " + Data.Sum().ToString());

            var spectrogramSize = width * height;
            var fftSize = data.Length;
            var offset = spectrogramSize - fftSize;
            WvlLogger.Log(LogType.TraceValues, "PrepareHeatmapDataSeries() - set offset : " + offset.ToString());

            try
            {
                Array.Copy(Data, fftSize, Data, 0, offset);
                Array.Copy(data, 0, Data, offset, fftSize);
                WvlLogger.Log(LogType.TraceValues, "PrepareHeatmapDataSeries() - Data after Array.Copy() : " + Data.Sum().ToString());

                //heatmapSeries.UpdateZValues(Data);
               samplesUpdated(this, new SamplesUpdatedEventArgs(Data));

                WvlLogger.Log(LogType.TraceAll, "PrepareHeatmapDataSeries() - UpdateZValues()");

            }
            catch (System.Exception e)
            {
                WvlLogger.Log(LogType.TraceExceptions, "PrepareHeatmapDataSeries() - exception : " + e.ToString());
            }
        }

        #endregion

        #region AudioRecord Recording / Playing

        //void OnNext()
        async void OnNext()
        {
            WvlLogger.Log(LogType.TraceAll, "OnNext()");
            /*
            short[] audioBuffer = new short[2048];
            //short[] audioBuffer = new short[1024];
            //audioRecord.Read(audioBuffer, 0, audioBuffer.Length);
            
            audioRecordCharts.Read(audioBuffer, 0, audioBuffer.Length);
            WvlLogger.Log(LogType.TraceValues, "OnNext() - audioRecordCharts.Read() - audioBUffer : " + audioBuffer.Length.ToString());
            int[] result = new int[audioBuffer.Length];
            for (int i = 0; i < audioBuffer.Length; i++)
            {
                result[i] = (int)audioBuffer[i];
            }
            bufferCount++;
            if (cumulBufferShort != null)
                cumulBufferShort = ArraysHelper.Combine(cumulBufferShort, audioBuffer);
            else
                cumulBufferShort = ArraysHelper.Init(audioBuffer);

            samplesUpdated(this, new SamplesUpdatedEventArgs(result));
            */

            //byte[] audioBufferDebug = new byte[2048];
            byte[] audioBufferDebug = new byte[1024];
            bufferCount++;
            audioRecord.Read(audioBufferDebug, 0, audioBufferDebug.Length);
            if (cumulBufferByte != null)
                cumulBufferByte = ArraysHelper.Combine(cumulBufferByte, audioBufferDebug);
            else
                cumulBufferByte = ArraysHelper.Init(audioBufferDebug);

            int[] bytesAsInts = Array.ConvertAll(audioBufferDebug, c => (int)c);

            samplesUpdated(this, new SamplesUpdatedEventArgs(bytesAsInts));


            /*
            samplesUpdatedThread = new Thread(() => samplesUpdated(this, new SamplesUpdatedEventArgs(bytesAsInts)));
            samplesUpdatedThread.Start();
            */

            //var res = FFT(bytesAsInts);
            

        }


        private void SamplesUpdated(Context context, EventArgs e)
        {

        }

        #region -> Recording

        //public void StartRecording()
        public async Task StartRecording()
        {

            
            WvlLogger.Log(LogType.TraceAll,"StartRecording()");

            //recorder = new AudioRecord(MediaRecorder.AudioSource.MIC,
            // RECORDER_SAMPLERATE, RECORDER_CHANNELS, RECORDER_AUDIO_ENCODING, bufferSize);

            audioRecord = new AudioRecord(
                AudioSource.Mic, 
                RECORDER_SAMPLERATE, 
                (ChannelIn)RECORDER_CHANNELS, 
                (Android.Media.Encoding)RECORDER_AUDIO_ENCODING, 
                bufferSize);

            WvlLogger.Log(LogType.TraceAll, "StartRecording() - AudioRecord : " + AudioSource.Mic.ToString() +
                                                        " - SampleRateInHz : " + RECORDER_SAMPLERATE.ToString() +
                                                        " - ChannelIn : " + RECORDER_CHANNELS.ToString() +
                                                        " - Encoding : " + RECORDER_AUDIO_ENCODING.ToString() +
                                                        " - buffer : " + bufferSize.ToString());


            audioRecordCharts = audioRecord;

            
            if (audioRecord.State == State.Initialized)
            {
                audioRecord.StartRecording();
            }
            /*
            if (audioRecordCharts.State == State.Initialized)
            {
                audioRecordCharts.StartRecording();
            }
            */
            isRecording = true;
                        
            recordingThread = new System.Threading.Thread(new ThreadStart(
                WriteAudioDataToFile
                ));

            /*
            chartsThread = new System.Threading.Thread(new ThreadStart(
                RepeatOnNext
                ));
            */

            recordingThread.Priority = System.Threading.ThreadPriority.Normal;
            recordingThread.IsBackground = true;
            recordingThread.Start();
            //chartsThread.Start();
            

            /*
            //while (audioRecord.RecordingState == RecordState.Recording)
            while (audioRecordCharts.RecordingState == RecordState.Recording)
            {
                try
                {
                    OnNext();
                }
                catch (Exception ex)
                {
                    throw ex;
                }
            }
            */
        }

        private void RepeatOnNext()
        {
            while (audioRecordCharts != null && audioRecordCharts.RecordingState == RecordState.Recording)
            {
                try
                {
                    OnNext();
                }
                catch (Exception ex)
                {
                    throw ex;
                }
            }
        }

        public void StopRecording()
        {
            WvlLogger.Log(LogType.TraceAll,"StopRecording()");
            
            if (null != audioRecord)
            {
                isRecording = false;
                if (audioRecord.State == State.Initialized)
                    audioRecord.Stop();
                audioRecord.Release();

                audioRecord = null;
                recordingThread = null;
            }
            
            /*
            if (null != audioRecordCharts)
            {
                if (audioRecordCharts.State == State.Initialized)
                {
                    audioRecordCharts.Stop();

                    // Write file after recording
                    isWriting = true;
                    WriteAudioDataToFileAfterRecording();
                }
                audioRecordCharts.Release();

                audioRecordCharts = null;
                chartsThread = null;

                samplesUpdatedThread = null;
            }
            */

            /*
            if (audioRecordCharts.State == State.Initialized)
            {
                audioRecordCharts.Stop();
                WriteAudioDataToFileAfterRecording();
            }
            audioRecordCharts.Release();
            */

            CopyWaveFile(GetTempFilename(), GetFilename());
            //DeleteTempFile();
        }

        #endregion

        #region -> Playing

        public async Task StartPlaying()
        {
            WvlLogger.Log(LogType.TraceAll,"StartPlaying()");

            string filePath = GetTempFilename();
            FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            BinaryReader binaryReader = new BinaryReader(fileStream);
            long totalBytes = new System.IO.FileInfo(filePath).Length;
            buffer = binaryReader.ReadBytes((Int32)totalBytes);
            fileStream.Close();
            fileStream.Dispose();
            binaryReader.Close();

            WvlLogger.Log(LogType.TraceValues, "StartPlaying() - " +
               " fileStream: " + fileStream.Name +
               " totalBytes: " + totalBytes.ToString());

            await PlayAudioTrackAsync();
        }

        protected async Task PlayAudioTrackAsync()
        {
            WvlLogger.Log(LogType.TraceAll,"PlayAudioTrackAsync()");
            WvlLogger.Log(LogType.TraceValues, "PlayAudioTrackAsync() - buffer.Length : " + buffer.Length.ToString());

            audioTrack = new AudioTrack(
                // Stream type
                Android.Media.Stream.Music,
                // Frequency
                RECORDER_SAMPLERATE,
                // Mono or stereo
                ChannelOut.Mono,
                // Audio encoding
                Android.Media.Encoding.Pcm16bit,
                // Length of the audio clip.
                buffer.Length,
                // Mode. Stream or static.
                AudioTrackMode.Stream);

            try
            {
                audioTrack.Play();
            }
            catch (Exception ex)
            {
                WvlLogger.Log(LogType.TraceAll, "PlayAudioTrackAsync() - audioTrack.Play() excetion : " + ex.ToString());
            }

            await audioTrack.WriteAsync(buffer, 0, buffer.Length);
        }

        public void StopPlaying()
        {
            WvlLogger.Log(LogType.TraceAll,"StopPlaying()");
            if (audioTrack != null)
            {
                audioTrack.Stop();
                audioTrack.Release();
                audioTrack = null;
            }
        }

        #endregion

        #endregion

        #region Files 

        #region -- Files creation/copy/delete

        private void WriteAudioDataToFile()
        {
            WvlLogger.Log(LogType.TraceAll,"WriteAudioDataToFile()");

            byte[] data = new byte[bufferSize];
            //short[] data = new short[bufferSize];
            string filename = GetTempFilename();
            FileOutputStream fos = null;

            try
            {
                fos = new FileOutputStream(filename);
            }
            catch (Java.IO.FileNotFoundException e)
            {
                // TODO Auto-generated catch block
                //e.printStackTrace();
                WvlLogger.Log(LogType.TraceExceptions,e.ToString());
            }

            int read = 0;

            if (null != fos)
            {
                while (isRecording)
                {
                    //WvlLogger.Log(LogType.TraceValues, "WriteAudioDataToFile() - audioRecord.Read - bufferSize : " + bufferSize.ToString());
                    read = audioRecord.Read(data, 0, bufferSize);
                    WvlLogger.Log(LogType.TraceValues, "WriteAudioDataToFile() - audioRecord.Read() " +
                                                        " - data : " + data.Length.ToString() +
                                                        " - bufferSize : " + bufferSize.ToString() +
                                                        " - read : " + read.ToString());

                    // OnNext treatment
                    //WvlLogger.Log(LogType.TraceAll, "EmbeddedOnNext()");

                    int[] bytesAsInts = Array.ConvertAll(data, c => (int)c);
                    /*
                    if (cumulBufferInt != null)
                        cumulBufferInt = ArraysHelper.Combine(cumulBufferInt, bytesAsInts);
                    else
                        cumulBufferInt = ArraysHelper.Init(bytesAsInts);
                    */

                    /*
                    var res = FFT(bytesAsInts);
                    //samplesUpdated(this, new SamplesUpdatedEventArgs(res));
                    */

                    samplesUpdatedThread = new Thread(() => FFT(bytesAsInts));
                    samplesUpdatedThread.Priority = System.Threading.ThreadPriority.Highest;
                    samplesUpdatedThread.Start();



                    //if (AudioRecord.ERROR_INVALID_OPERATION != read)
                    if ((int)RecordStatus.ErrorInvalidOperation != read)
                    {
                        try
                        {
                            // data = byte[]
                            WvlLogger.Log(LogType.TraceValues, "WriteAudioDataToFile() - fos.Write(dataByte) " +
                                        " - dataByte : " + data.Length.ToString());
                            fos.Write(data);
                            // dataByte = byte[]
                            /*
                            byte[] dataByte = Array.ConvertAll(data, item => (byte)item);
                            WvlLogger.Log(LogType.TraceValues, "WriteAudioDataToFile() - fos.Write(dataByte) " +
                                         " - dataByte : " + dataByte.Length.ToString());
                            fos.Write(dataByte);
                            */

                            /*
                            // dataShort = short[]                               
                            short[] dataShort = new short[bufferSize];
                            dataShort = Array.ConvertAll(data, d => (short)d);
                            int[] result = new int[dataShort.Length];
                            for (int i = 0; i < dataShort.Length; i++)
                            {
                                result[i] = (int)dataShort[i];
                            }                   
                            samplesUpdated(this, new SamplesUpdatedEventArgs(result));
                            */
                        }
                        catch (Java.IO.IOException e)
                        {
                            //e.printStackTrace();
                            WvlLogger.Log(LogType.TraceExceptions,"WriteAudioDataToFile - Exception on fos.Write() : " + e.ToString());
                        }
                    }
                }

                try
                {
                    fos.Close();
                }
                catch (Java.IO.IOException e)
                {
                    //e.printStackTrace();
                    WvlLogger.Log(LogType.TraceExceptions, "WriteAudioDataToFile - Exception on fos.Close() : " + e.ToString());
                }
            }
        }

        private void WriteAudioDataToFileAfterRecording()
        {
            WvlLogger.Log(LogType.TraceAll, "WriteAudioDataToFileAfterRecording()");

            byte[] data = new byte[bufferSize];
            //short[] data = new short[bufferSize];
            string filename = GetTempFilename();
            FileOutputStream fos = null;

            try
            {
                fos = new FileOutputStream(filename);
            }
            catch (Java.IO.FileNotFoundException e)
            {
                // TODO Auto-generated catch block
                //e.printStackTrace();
                WvlLogger.Log(LogType.TraceExceptions, e.ToString());
            }

            int read = 0;

            if (null != fos)
            {
                /*
                //audioRecordCharts.
                //while (isWriting)
                for (int i = 0; i <= bufferCount; i++)
                {
                    //WvlLogger.Log(LogType.TraceValues, "WriteAudioDataToFile() - audioRecord.Read - bufferSize : " + bufferSize.ToString());
                    //read = audioRecordCharts.Read(data, 0, bufferSize);
                    read = audioRecordCharts.Read(data, cumulBufferSize, bufferSize);
                    cumulBufferSize += bufferSize - 1;
                    WvlLogger.Log(LogType.TraceValues, "WriteAudioDataToFileAfterRecording() - audioRecord.Read() " +
                                                        " - data : " + data.Length.ToString() +
                                                        " - bufferSize : " + bufferSize.ToString() +
                                                        " - read : " + read.ToString());

                    // OnNext
                    //WvlLogger.Log(LogType.TraceAll, "EmbeddedOnNext()");

                    
                    ////audioRecord.Read(audioBuffer, 0, audioBuffer.Length);

                    ////int[] result = new int[bufferSize];
                    ////short[] shortByte = Array.ConvertAll(data, b => (short)b);
                    ////for (int i = 0; i < bufferSize; i++)
                    ////{
                    ////    result[i] = (int)shortByte[i];
                    ////}
                    

                    // data = short[]   
                    
                    ////int[] result = new int[data.Length];
                    ////for (int i = 0; i < data.Length; i++)
                    ////{
                    ////    result[i] = (int)data[i];
                    ////}                   
                    ////samplesUpdated(this, new SamplesUpdatedEventArgs(result));
                    

                    // dataShort = short[]   
                    
                    ////short[] dataShort = new short[bufferSize];
                    ////dataShort = Array.ConvertAll(data, d => (short)d);
                    ////int[] result = new int[dataShort.Length];
                    ////for (int i = 0; i < dataShort.Length; i++)
                    ////{
                    ////    result[i] = (int)dataShort[i];
                    ////}                   
                    ////samplesUpdated(this, new SamplesUpdatedEventArgs(result));
                    

                    //if (AudioRecord.ERROR_INVALID_OPERATION != read)
                    if ((int)RecordStatus.ErrorInvalidOperation != read)
                    {
                        try
                        {
                            // data = byte[]
                            WvlLogger.Log(LogType.TraceValues, "WriteAudioDataToFileAfterRecording() - fos.Write(dataByte) " +
                                        " - dataByte : " + data.Length.ToString());
                            fos.Write(data);
                            // dataByte = byte[]
                            
                            ////byte[] dataByte = Array.ConvertAll(data, item => (byte)item);
                            ////WvlLogger.Log(LogType.TraceValues, "WriteAudioDataToFile() - fos.Write(dataByte) " +
                            ////             " - dataByte : " + dataByte.Length.ToString());
                            ////fos.Write(dataByte);
                            

                            
                            ////// dataShort = short[]                               
                            ////short[] dataShort = new short[bufferSize];
                            ////dataShort = Array.ConvertAll(data, d => (short)d);
                            ////int[] result = new int[dataShort.Length];
                            ////for (int i = 0; i < dataShort.Length; i++)
                            ////{
                            ////    result[i] = (int)dataShort[i];
                            ////}                   
                            ////samplesUpdated(this, new SamplesUpdatedEventArgs(result));
                            
                        }
                        catch (Java.IO.IOException e)
                        {
                            //e.printStackTrace();
                            WvlLogger.Log(LogType.TraceExceptions, "WriteAudioDataToFileAfterRecording - Exception on fos.Write() : " + e.ToString());
                        }
                    }
                }
                */

                for (int i = 0; i < bufferCount; i++)
                {
                    /*
                    //Span<short> ss = cumulBufferShort.Slice
                    //var curBufferShort = cumulBufferShort.Slice(i * bufferSize, ((i + 1) * bufferSize) - 1);
                    var curBufferShort = cumulBufferShort.Slice(i * bufferSize, ((i + 1) * bufferSize));

                    data = new byte[curBufferShort.Length * sizeof(short)];
                    Buffer.BlockCopy(curBufferShort, 0, data, 0, data.Length);
                    */

                    data = cumulBufferByte.Slice(i * bufferSize, ((i+1) * bufferSize));

                    try
                    {
                        fos.Write(data);
                    }
                    catch (Java.IO.IOException e)
                    {
                        //e.printStackTrace();
                        WvlLogger.Log(LogType.TraceExceptions, "WriteAudioDataToFileAfterRecording - Exception on fos.Write() : " + e.ToString());
                    }

                }

                try
                {
                    fos.Close();
                }
                catch (Java.IO.IOException e)
                {
                    //e.printStackTrace();
                    WvlLogger.Log(LogType.TraceExceptions, "WriteAudioDataToFileAfterRecording - Exception on fos.Close() : " + e.ToString());
                }
            }
        }
        private void DeleteTempFile()
        {
            WvlLogger.Log(LogType.TraceAll,"DeleteTempFile()");
            Java.IO.File file = new Java.IO.File(GetTempFilename());

            file.Delete();
        }

        private void CopyWaveFile(string inFilename, string outFilename)
        {
            WvlLogger.Log(LogType.TraceAll,"CopyWaveFile()");

            FileInputStream fis = null;
            FileOutputStream fos = null;


            long totalAudioLen = 0;
            long totalDataLen = totalAudioLen + 36;
            long longSampleRate = RECORDER_SAMPLERATE;
            //int channels = 2;
            int channels = 1;
            long byteRate = RECORDER_BPP * RECORDER_SAMPLERATE * channels / 8;

            byte[] data = new byte[bufferSize];

            try
            {
                fis = new FileInputStream(inFilename);
                fos = new FileOutputStream(outFilename);
                totalAudioLen = fis.Channel.Size();
                totalDataLen = totalAudioLen + 36;

                WvlLogger.Log(LogType.TraceValues,"CopyWaveFile() - File size: " + totalDataLen.ToString());

                WriteWaveFileHeader(fos, totalAudioLen, totalDataLen,
                    longSampleRate, channels, byteRate);

                while (fis.Read(data) != -1)
                {
                    fos.Write(data);
                }

                fis.Close();
                fos.Close();
            }
            catch (Java.IO.FileNotFoundException e)
            {
                //e.printStackTrace();
                WvlLogger.Log(LogType.TraceExceptions,"CopyWaveFile() - FileNotFoundException: " + e.ToString());
            }
            catch (Java.IO.IOException e)
            {
                //e.printStackTrace();
                WvlLogger.Log(LogType.TraceExceptions, "CopyWaveFile() - IOException: " + e.ToString());
            }
        }

        private void WriteWaveFileHeader(FileOutputStream fos, long totalAudioLen,
            long totalDataLen, long longSampleRate, int channels, long byteRate)
        {
            WvlLogger.Log(LogType.TraceAll,"WriteWaveFileHeader()");
            try
            {
                byte[] header = new byte[44];

                header[0] = (byte)'R'; // RIFF/WAVE header
                header[1] = (byte)'I';
                header[2] = (byte)'F';
                header[3] = (byte)'F';
                header[4] = (byte)(totalDataLen & 0xff);
                header[5] = (byte)((totalDataLen >> 8) & 0xff);
                header[6] = (byte)((totalDataLen >> 16) & 0xff);
                header[7] = (byte)((totalDataLen >> 24) & 0xff);
                header[8] = (byte)'W';
                header[9] = (byte)'A';
                header[10] = (byte)'V';
                header[11] = (byte)'E';
                header[12] = (byte)'f'; // 'fmt ' chunk
                header[13] = (byte)'m';
                header[14] = (byte)'t';
                header[15] = (byte)' ';
                header[16] = 16; // 4 bytes: size of 'fmt ' chunk
                header[17] = 0;
                header[18] = 0;
                header[19] = 0;
                header[20] = 1; // format = 1
                header[21] = 0;
                header[22] = (byte)channels;
                header[23] = 0;
                header[24] = (byte)(longSampleRate & 0xff);
                header[25] = (byte)((longSampleRate >> 8) & 0xff);
                header[26] = (byte)((longSampleRate >> 16) & 0xff);
                header[27] = (byte)((longSampleRate >> 24) & 0xff);
                header[28] = (byte)(byteRate & 0xff);
                header[29] = (byte)((byteRate >> 8) & 0xff);
                header[30] = (byte)((byteRate >> 16) & 0xff);
                header[31] = (byte)((byteRate >> 24) & 0xff);
                header[32] = (byte)(2 * 16 / 8); // block align
                header[33] = 0;
                header[34] = (byte)RECORDER_BPP; // bits per sample
                header[35] = 0;
                header[36] = (byte)'d';
                header[37] = (byte)'a';
                header[38] = (byte)'t';
                header[39] = (byte)'a';
                header[40] = (byte)(totalAudioLen & 0xff);
                header[41] = (byte)((totalAudioLen >> 8) & 0xff);
                header[42] = (byte)((totalAudioLen >> 16) & 0xff);
                header[43] = (byte)((totalAudioLen >> 24) & 0xff);

                fos.Write(header, 0, 44);
            }
            catch (System.Exception e)
            {
                WvlLogger.Log(LogType.TraceExceptions, "WriteWaveFileHeader() - Exception: " + e.ToString());
            }
        }

        #endregion

        #region -- Filenames

        private string GetFilename()
        {
            WvlLogger.Log(LogType.TraceAll,"GetFilename()");
            string filepath = Android.OS.Environment.ExternalStorageDirectory.Path;
            Java.IO.File file = new Java.IO.File(filepath, AUDIO_RECORDER_FOLDER);

            if (!file.Exists())
            {
                file.Mkdirs();
            }

            var result = (file.AbsolutePath + "/" + DateTime.Now.Millisecond.ToString() + AUDIO_RECORDER_FILE_EXT_WAV);
            wavFileName = result;
            WvlLogger.Log(LogType.TraceAll,"GetFilename() : " + result);
            return result;
        }

        private string GetTempFilename()
        {
            WvlLogger.Log(LogType.TraceAll,"GetTempFilename()");
            string filepath = Android.OS.Environment.ExternalStorageDirectory.Path;
            Java.IO.File file = new Java.IO.File(filepath, AUDIO_RECORDER_FOLDER);

            if (!file.Exists())
            {
                file.Mkdirs();
            }

            Java.IO.File tempFile = new Java.IO.File(filepath, AUDIO_RECORDER_TEMP_FILE);

            if (tempFile.Exists())
                tempFile.Delete();

            var result = (file.AbsolutePath + "/" + AUDIO_RECORDER_TEMP_FILE);
            WvlLogger.Log(LogType.TraceAll,"GetTempFilename() : " + result);
            return result;
        }

        #endregion

        #endregion

    }
}