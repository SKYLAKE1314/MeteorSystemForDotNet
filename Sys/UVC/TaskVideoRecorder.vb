Imports OpenCvSharp
Imports OpenCvSharp.WpfExtensions

Public Class TaskVideoRecorder

    Public Class RecordingInfo
        Public Property StreamUrl As String
        Public Property StreamStartTime As Long
        Public Property StreamStatus As String
        Public Property VideoFormat As String
        Public Property BitRate As Integer
        Public Property Resolution As String
        Public Property FrameRate As Integer
        Public Property CameraId As String
    End Class

    Private Shared ReadOnly _instance As New TaskVideoRecorder()
    Public Shared ReadOnly Property Instance As TaskVideoRecorder
        Get
            Return _instance
        End Get
    End Property

    Private ReadOnly _syncRoot As New Object()
    Private _cts As Threading.CancellationTokenSource
    Private _worker As Task
    Private _currentInfo As RecordingInfo
    Private _currentFilePath As String

    Private Sub New()
    End Sub

    Public Async Function StartRecordingAsync(cameraId As String, filePath As String) As Task(Of RecordingInfo)
        If String.IsNullOrWhiteSpace(cameraId) OrElse String.IsNullOrWhiteSpace(filePath) Then Return Nothing

        Await StopRecordingAsync()

        Dim directory = IO.Path.GetDirectoryName(filePath)
        If Not String.IsNullOrWhiteSpace(directory) Then
            IO.Directory.CreateDirectory(directory)
        End If

        CameraService.Instance.StartCamera(cameraId)

        Dim firstFrame = Await WaitForFirstFrameAsync(cameraId, 2000)
        Dim resolution = ""
        If firstFrame IsNot Nothing Then
            Using mat = BitmapSourceConverter.ToMat(firstFrame)
                resolution = $"{mat.Width}x{mat.Height}"
            End Using
        End If

        Dim info As New RecordingInfo With {
            .StreamUrl = filePath,
            .StreamStartTime = DateTimeOffset.Now.ToUnixTimeMilliseconds(),
            .StreamStatus = "RUNNING",
            .VideoFormat = "MP4",
            .BitRate = 1024,
            .Resolution = resolution,
            .FrameRate = 25,
            .CameraId = cameraId
        }

        SyncLock _syncRoot
            _cts = New Threading.CancellationTokenSource()
            _currentInfo = info
            _currentFilePath = filePath
            _worker = Task.Run(Function() RecordLoopAsync(cameraId, filePath, info, _cts.Token))
        End SyncLock

        Return info
    End Function

    Public Async Function StopRecordingAsync() As Task
        Dim worker As Task = Nothing

        SyncLock _syncRoot
            If _cts IsNot Nothing Then
                _cts.Cancel()
            End If
            worker = _worker
            _worker = Nothing
            _cts = Nothing
        End SyncLock

        If worker IsNot Nothing Then
            Try
                Await worker
            Catch ex As Exception
                Logger.Warn("[VideoRecorder] stop wait failed: " & ex.Message)
            End Try
        End If

        SyncLock _syncRoot
            If _currentInfo IsNot Nothing Then
                _currentInfo.StreamStatus = "STOPPED"
            End If
        End SyncLock
    End Function

    Public Function GetCurrentInfo() As RecordingInfo
        SyncLock _syncRoot
            Return _currentInfo
        End SyncLock
    End Function

    Private Async Function WaitForFirstFrameAsync(cameraId As String, timeoutMs As Integer) As Task(Of BitmapSource)
        Dim sw As New Stopwatch()
        sw.Start()

        While sw.ElapsedMilliseconds < timeoutMs
            Dim frame = CameraService.Instance.GetFrame(cameraId)
            If frame IsNot Nothing Then Return frame
            Await Task.Delay(50)
        End While

        Return Nothing
    End Function

    Private Async Function RecordLoopAsync(cameraId As String,
                                           filePath As String,
                                           info As RecordingInfo,
                                           token As Threading.CancellationToken) As Task
        Dim writer As VideoWriter = Nothing

        Try
            Dim frameInterval = CInt(Math.Max(1, 1000 / Math.Max(1, info.FrameRate)))

            While Not token.IsCancellationRequested
                Dim frame = CameraService.Instance.GetFrame(cameraId)

                If frame IsNot Nothing Then
                    Using mat = BitmapSourceConverter.ToMat(frame)
                        If writer Is Nothing Then
                            writer = New VideoWriter(
                                filePath,
                                VideoWriter.Fourcc("m"c, "p"c, "4"c, "v"c),
                                info.FrameRate,
                                New OpenCvSharp.Size(mat.Width, mat.Height))

                            If String.IsNullOrWhiteSpace(info.Resolution) Then
                                info.Resolution = $"{mat.Width}x{mat.Height}"
                            End If
                        End If

                        If writer.IsOpened() Then
                            writer.Write(mat)
                        End If
                    End Using
                End If

                Await Task.Delay(frameInterval, token)
            End While

        Catch ex As TaskCanceledException
        Catch ex As Exception
            Logger.Error("[VideoRecorder] record loop failed: " & ex.Message)
        Finally
            If writer IsNot Nothing Then
                writer.Release()
                writer.Dispose()
            End If
        End Try
    End Function

End Class
