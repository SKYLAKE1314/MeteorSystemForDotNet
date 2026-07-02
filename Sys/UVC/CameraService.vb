Public Class CameraService

    Private Shared ReadOnly _inst As New CameraService()

    Public Shared ReadOnly Property Instance As CameraService
        Get
            Return _inst
        End Get
    End Property

    Private _cameras As New Dictionary(Of String, CameraLink)
    Private _frames As New Dictionary(Of String, BitmapSource)

    Public Event FrameArrived As Action(Of String, BitmapSource)

    Private Sub New()
    End Sub

    ' =========================
    ' 啟動全部相機
    ' =========================
    Public Sub StartAll()

        Dim ids = My.Settings.CameraDeviceIds
        If ids Is Nothing OrElse ids.Count = 0 Then Return

        For Each id In ids

            If _cameras.ContainsKey(id) Then Continue For

            Dim cam As New CameraLink(id)

            AddHandler cam.FrameArrived,
                Sub(deviceId As String, img As BitmapSource)

                    SyncLock _frames
                        _frames(deviceId) = img
                    End SyncLock

                    RaiseEvent FrameArrived(deviceId, img)

                End Sub

            cam.StartCamera(id)
            _cameras(id) = cam

        Next

    End Sub

    ' =========================
    ' 取得指定相機畫面
    ' =========================
    Public Function GetFrame(deviceId As String) As BitmapSource

        SyncLock _frames
            If _frames.ContainsKey(deviceId) Then
                Return _frames(deviceId)
            End If
        End SyncLock

        Return Nothing

    End Function

    Public Sub StopAll()

        For Each cam In _cameras.Values
            cam.StopCamera()
        Next

        _cameras.Clear()

    End Sub

End Class