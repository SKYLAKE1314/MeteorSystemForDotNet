Imports System.Windows
Imports System.Windows.Controls
Imports System.ComponentModel

Public Class SettingPage

    Private _isLoaded As Boolean = False
    Private _cameraList As New List(Of CameraInfo)

    Public Sub New()
        InitializeComponent()

        AddHandler Me.Loaded, AddressOf SettingPage_Loaded
    End Sub

    ' =========================
    ' Page Loaded
    ' =========================
    Private Sub SettingPage_Loaded(
    sender As Object,
    e As RoutedEventArgs)

        _isLoaded = True

        Select Case LanguageManager.CurrentLanguage
            Case "zhTW"
                LanguageComboBox.SelectedIndex = 0
            Case "zhCN"
                LanguageComboBox.SelectedIndex = 1
            Case "enUS"
                LanguageComboBox.SelectedIndex = 2
            Case Else
                LanguageComboBox.SelectedIndex = 0
        End Select

        LoadCameraList()

        Dim savedId As String = My.Settings.CameraDeviceId

        For i As Integer = 0 To CameraComboBox.Items.Count - 1

            Dim item As ComboBoxItem =
        CType(CameraComboBox.Items(i), ComboBoxItem)

            If item.Tag IsNot Nothing AndAlso
       item.Tag.ToString() = savedId Then

                CameraComboBox.SelectedIndex = i
                Exit For

            End If

        Next

    End Sub

    ' =========================
    ' Language Switch
    ' =========================
    Private Sub LanguageComboBox_SelectionChanged(sender As Object, e As SelectionChangedEventArgs)

        If DesignerProperties.GetIsInDesignMode(Me) Then Return
        If Not _isLoaded Then Return

        Select Case LanguageComboBox.SelectedIndex
            Case 0 : LanguageManager.Load("zhTW")
                Logger.Debug("正體中文")
            Case 1 : LanguageManager.Load("zhCN")
            Case 2 : LanguageManager.Load("enUS")
        End Select

        Dim main = TryCast(Application.Current?.MainWindow, MainWindow)
        If main IsNot Nothing Then
            main.RefreshLanguageUI()
        End If

    End Sub

    Private Sub CameraComboBox_SelectionChanged(
    sender As Object,
    e As SelectionChangedEventArgs)

        If Not _isLoaded Then Return

        Dim item =
        TryCast(CameraComboBox.SelectedItem, ComboBoxItem)

        If item Is Nothing Then Return

        My.Settings.CameraDeviceId = item.Tag.ToString()
        My.Settings.Save()

        CameraManager.NotifyCameraChanged()

        Logger.Info(
        $"Camera DeviceId = {My.Settings.CameraDeviceId}")

    End Sub



    Private Sub LoadCameraList()

        _cameraList = CameraManager.GetCachedCameras()

        CameraComboBox.Items.Clear()

        For Each cam In _cameraList

            CameraComboBox.Items.Add(
        New ComboBoxItem With {
            .Content = cam.Name,
            .Tag = cam.DeviceId    '  存 DeviceId
        })

        Next

    End Sub

End Class