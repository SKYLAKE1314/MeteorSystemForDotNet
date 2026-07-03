Imports System.Management
Imports OpenCvSharp

Public Class CameraManager

    Public Shared Event CameraChanged As Action

    Private Shared _cameraCache As List(Of CameraInfo)
    Private Shared _initialized As Boolean = False

    Public Shared Sub Initialize()

        If _initialized Then Return
        _initialized = True

        _cameraCache = GetCameras()

    End Sub

    Public Shared Sub NotifyCameraChanged()
        RaiseEvent CameraChanged()
    End Sub

    Public Shared Function GetCameras() As List(Of CameraInfo)

        Dim result As New List(Of CameraInfo)

        Dim searcher As New ManagementObjectSearcher(
            "SELECT * FROM Win32_PnPEntity WHERE PNPClass='Camera' OR PNPClass='Image'")

        Dim deviceList As New List(Of CameraInfo)

        For Each obj As ManagementObject In searcher.Get()
            deviceList.Add(New CameraInfo With {
                .Name = obj("Name")?.ToString(),
                .DeviceId = obj("PNPDeviceID")?.ToString(),
                .Index = -1
            })
        Next

        ' 依緒探测 OpenCV index 0..N，根據順序對應 WMI 設備
        ' 同名相機使用 PNPDeviceID 區分（已是唯一統一碼）
        Dim openIndex As Integer = 0
        Dim unmapped As New Queue(Of CameraInfo)(deviceList)

        While unmapped.Count > 0
            Try
                Using cap As New VideoCapture(openIndex, VideoCaptureAPIs.DSHOW)
                    If cap.IsOpened() Then
                        Dim cam = unmapped.Dequeue()
                        cam.Index = openIndex
                        result.Add(cam)
                    Else
                        Exit While
                    End If
                End Using
            Catch ex As Exception
                Logger.Warn($"[CameraManager] index {openIndex} probe failed: {ex.Message}")
                Exit While
            End Try
            openIndex += 1
        End While

        Return result

    End Function

    Public Shared Function FindIndexByDeviceId(deviceId As String) As Integer

        If _cameraCache Is Nothing Then Return -1

        Dim cam = _cameraCache.FirstOrDefault(Function(x) x.DeviceId = deviceId)

        If cam Is Nothing Then Return -1

        Return cam.Index

    End Function

    Public Shared Function GetCachedCameras() As List(Of CameraInfo)
        Return If(_cameraCache, New List(Of CameraInfo))
    End Function

    Public Shared Sub Refresh()
        _cameraCache = GetCameras()
        For Each cam In _cameraCache
            Logger.Info($"Camera Found: {cam.Name} | {cam.DeviceId} | Index:{cam.Index}")
        Next

        NotifyCameraChanged()
    End Sub

End Class