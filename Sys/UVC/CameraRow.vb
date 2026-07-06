Imports System.ComponentModel

Public Class CameraRow
    Implements INotifyPropertyChanged

    Public Event PropertyChanged As PropertyChangedEventHandler Implements INotifyPropertyChanged.PropertyChanged

    Private Sub Notify(name As String)
        RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(name))
    End Sub

    Public Property Title As String

    Private _cameraList As List(Of CameraInfo)
    Public Property CameraList As List(Of CameraInfo)
        Get
            Return _cameraList
        End Get
        Set(value As List(Of CameraInfo))
            _cameraList = value
            Notify(NameOf(CameraList))
        End Set
    End Property

    Private _selectedCamera As CameraInfo
    Public Property SelectedCamera As CameraInfo
        Get
            Return _selectedCamera
        End Get
        Set(value As CameraInfo)
            _selectedCamera = value
            Notify(NameOf(SelectedCamera))
        End Set
    End Property

    Private _resolutionList As List(Of CameraResolutionOption) = CameraResolutionOption.CommonResolutions()
    Public Property ResolutionList As List(Of CameraResolutionOption)
        Get
            Return _resolutionList
        End Get
        Set(value As List(Of CameraResolutionOption))
            _resolutionList = value
            Notify(NameOf(ResolutionList))
        End Set
    End Property

    Private _selectedResolution As CameraResolutionOption
    Public Property SelectedResolution As CameraResolutionOption
        Get
            Return _selectedResolution
        End Get
        Set(value As CameraResolutionOption)
            _selectedResolution = value
            Notify(NameOf(SelectedResolution))
        End Set
    End Property

    Private _addVisible As Visibility
    Public Property AddVisible As Visibility
        Get
            Return _addVisible
        End Get
        Set(value As Visibility)
            _addVisible = value
            Notify(NameOf(AddVisible))
        End Set
    End Property

    Private _removeVisible As Visibility
    Public Property RemoveVisible As Visibility
        Get
            Return _removeVisible
        End Get
        Set(value As Visibility)
            _removeVisible = value
            Notify(NameOf(RemoveVisible))
        End Set
    End Property

End Class