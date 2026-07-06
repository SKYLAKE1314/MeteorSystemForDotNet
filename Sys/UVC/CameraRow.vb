Public Class CameraRow

    Public Property Title As String
    Public Property CameraList As List(Of CameraInfo)

    Public Property SelectedCamera As CameraInfo

    Public Property ResolutionList As List(Of CameraResolutionOption) =
        CameraResolutionOption.CommonResolutions()
    Public Property SelectedResolution As CameraResolutionOption

    Public Property AddVisible As Visibility
    Public Property RemoveVisible As Visibility

End Class