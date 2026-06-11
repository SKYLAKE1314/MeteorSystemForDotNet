Imports System.Management
Imports OpenCvSharp

Public Class CameraManager

    Public Shared Event CameraChanged As Action

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
            .DeviceId = obj("PNPDeviceID")?.ToString()
        })

        Next

        ' match OpenCV index
        Dim index As Integer = 0

        For Each cam In deviceList

            Using cap As New VideoCapture(index)

                If cap.IsOpened() Then

                    cam.Index = index
                    result.Add(cam)

                    index += 1

                End If

            End Using

        Next

        Return result

    End Function

    Public Shared Function FindIndexByDeviceId(deviceId As String) As Integer

        Dim list = GetCameras()

        Dim cam = list.FirstOrDefault(Function(x) x.DeviceId = deviceId)

        If cam Is Nothing Then Return -1

        Return cam.Index

    End Function

    Private Shared _cameraCache As List(Of CameraInfo)

    Private Shared _initialized As Boolean = False

    Public Shared Sub Initialize()

        If _initialized Then Return
        _initialized = True

        _cameraCache = GetCameras()

    End Sub

    Public Shared Function GetCachedCameras() As List(Of CameraInfo)
        Return _cameraCache
    End Function


End Class