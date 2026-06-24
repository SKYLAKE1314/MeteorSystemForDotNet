Imports System.Windows
Imports System.Windows.Controls
Imports System.ComponentModel
Imports System.Collections.ObjectModel

Public Class SettingPage

    Private _isLoaded As Boolean = False

    Private _ioMode As IoBoardMode = IoBoardMode.IO
    Private _cameraList As New List(Of CameraInfo)

    Public Property CameraRows As New ObservableCollection(Of CameraRow)

    Public Sub New()
        InitializeComponent()

        CameraRows = New ObservableCollection(Of CameraRow)

        Me.DataContext = Me   ' ⭐ 讀settings

        _isLoaded = True
        ' IO卡
        Dim modeStr = My.Settings.IoMode

        If Not [Enum].TryParse(modeStr, _ioMode) Then
            _ioMode = IoBoardMode.IO
        End If

        LoadCameraRows()

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

        LoadCameraRows()

        Dim savedId As String = My.Settings.CameraDeviceId
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
    ' 板卡

    Private Sub IoBoardComboBox_SelectionChanged(sender As Object, e As SelectionChangedEventArgs)

        If IoBoardComboBox.SelectedItem Is Nothing Then Return

        Dim text = TryCast(CType(IoBoardComboBox.SelectedItem, ComboBoxItem).Content, String)

        Select Case text
            Case "IO"
                _ioMode = IoBoardMode.IO

            Case "PLC"
                _ioMode = IoBoardMode.PLC

            Case "NONE"
                _ioMode = IoBoardMode.NONE
        End Select

        My.Settings.IoMode = _ioMode.ToString()
        My.Settings.Save()

    End Sub

    Private Sub Camera_SelectionChanged(sender As Object, e As SelectionChangedEventArgs)

        If Not _isLoaded Then Return

        Dim cb = DirectCast(sender, ComboBox)
        Dim row = DirectCast(cb.DataContext, CameraRow)

        Dim cam = TryCast(cb.SelectedItem, CameraInfo)
        row.SelectedCamera = cam

        RefreshAllCameraLists()

        My.Settings.CameraDeviceId = cam?.DeviceId
        My.Settings.Save()

    End Sub

    Private Sub LoadCameraRows()

        Dim list = CameraManager.GetCachedCameras()

        CameraRows.Clear()

        Dim savedId = My.Settings.CameraDeviceId

        Dim first = New CameraRow With {
        .Title = "相機 1",
        .CameraList = list
    }

        CameraRows.Add(first)

        ' 還原選擇
        first.SelectedCamera =
        list.FirstOrDefault(Function(c) c.DeviceId = savedId)

        UpdateCameraButtons()

        RefreshAllCameraLists()

    End Sub

    Private Function GetAvailableCameras(currentRow As CameraRow) As List(Of CameraInfo)

        Dim all = CameraManager.GetCachedCameras()

        Dim used = CameraRows.
        Where(Function(r) r.SelectedCamera IsNot Nothing AndAlso r IsNot currentRow).
        Select(Function(r) r.SelectedCamera.DeviceId).
        ToList()

        Return all.
        Where(Function(c) Not used.Contains(c.DeviceId)).
        ToList()

    End Function

    Private Sub UpdateCameraButtons()

        For i = 0 To CameraRows.Count - 1

            CameraRows(i).Title = $"相機 {i + 1}"

            ' ➕ 永遠只在最後一個
            CameraRows(i).AddVisible =
            If(i = CameraRows.Count - 1,
               Visibility.Visible,
               Visibility.Collapsed)

            ' ➖ 第一個永遠不能刪
            CameraRows(i).RemoveVisible =
            If(i = 0,
               Visibility.Collapsed,
               Visibility.Visible)

        Next

    End Sub
    ' 新增相機
    Private Sub AddCamera()

        Dim list = CameraManager.GetCachedCameras()

        CameraRows.Add(New CameraRow With {
        .Title = $"相機 {CameraRows.Count + 1}",
        .CameraList = list
    })

        UpdateCameraButtons()

    End Sub

    ' 相機控件刪減
    Private Sub AddCamera_Click(sender As Object, e As RoutedEventArgs)
        AddCamera()
    End Sub

    Private Sub RemoveCamera_Click(sender As Object, e As RoutedEventArgs)

        Dim btn = DirectCast(sender, Button)
        Dim row = TryCast(btn.DataContext, CameraRow)

        If row Is Nothing Then Return

        RemoveCamera(row)

    End Sub

    Private Sub RemoveCamera(row As CameraRow)

        If CameraRows.Count <= 1 Then Return

        CameraRows.Remove(row)

        UpdateCameraButtons()

    End Sub

    Private Sub RefreshAllCameraLists()

        For Each row In CameraRows
            row.CameraList = GetAvailableCameras(row)
        Next

    End Sub

    Private Sub GpuBoostComboBox_SelectionChanged(sender As Object, e As SelectionChangedEventArgs)

    End Sub
End Class