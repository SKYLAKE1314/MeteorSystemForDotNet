Imports System.Management
Imports OpenCvSharp
Public Class CameraManager
    Public Shared Event CameraChanged As Action ' 相機列表變更事件
    Public Shared Sub NotifyCameraChanged()
        RaiseEvent CameraChanged()
    End Sub

    Public Shared Function GetCameras() As List(Of CameraInfo)

        Dim result As New List(Of CameraInfo)

        Dim searcher As New ManagementObjectSearcher(
            "SELECT * FROM Win32_PnPEntity WHERE PNPClass='Camera' OR PNPClass='Image'")

        Dim deviceList As New List(Of CameraInfo)

        For Each obj As ManagementObject In searcher.Get()

            Dim info As New CameraInfo()

            info.Name =
                obj("Name")?.ToString()

            info.DeviceId =
                obj("PNPDeviceID")?.ToString()

            deviceList.Add(info)

        Next

        ' 嘗試對應 OpenCV Index
        Dim currentIndex As Integer = 0

        For Each device In deviceList

            Using cap As New VideoCapture(currentIndex)

                If cap.IsOpened() Then

                    device.Index = currentIndex

                    result.Add(device)

                    currentIndex += 1

                End If

            End Using

        Next

        Return result

    End Function

    Public Shared Function FindIndexByDeviceId(
    deviceId As String) As Integer

        Dim list =
        GetCameras()

        Dim cam =
        list.FirstOrDefault(
            Function(x)
                Return x.DeviceId = deviceId
            End Function)

        If cam Is Nothing Then
            Return -1
        End If

        Return cam.Index

    End Function


End Class
