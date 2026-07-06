Imports System.Windows
Imports System.Windows.Controls
Imports System.Windows.Media

Public Class CameraPickDialog
    Inherits System.Windows.Window

    Public Property SelectedCameraId As String = ""
    Public Property SelectedCameraSlot As Integer = 0

    Private _combo As ComboBox

    Public Sub New()
        Me.Title = "選擇相機"
        Me.Width = 360
        Me.Height = 200
        Me.WindowStartupLocation = WindowStartupLocation.CenterOwner
        Me.ResizeMode = ResizeMode.NoResize
        Me.Background = New SolidColorBrush(Color.FromRgb(&HFF, &HFF, &HFF))

        Dim panel As New StackPanel()
        panel.Margin = New Thickness(20)
        panel.Background = Brushes.White

        Dim title As New TextBlock()
        title.Text = "選擇拍攝相機"
        title.FontSize = 14
        title.FontWeight = FontWeights.SemiBold
        title.Foreground = Brushes.Black
        title.Margin = New Thickness(0, 0, 0, 12)
        panel.Children.Add(title)

        ' ComboBox
        _combo = New ComboBox()
        _combo.Height = 30
        _combo.Margin = New Thickness(0, 0, 0, 16)
        _combo.Background = New SolidColorBrush(Color.FromRgb(&HFF, &HFF, &HFF))
        _combo.Foreground = Brushes.Black
        _combo.BorderBrush = New SolidColorBrush(Color.FromRgb(&HB0, &HB0, &HB0))
        _combo.BorderThickness = New Thickness(1)

        ' 先設定 DisplayMemberPath 再加 Items
        _combo.DisplayMemberPath = "Label"

        Dim ids = My.Settings.CameraDeviceIds
        Dim cameras = CameraManager.GetCachedCameras()

        If ids IsNot Nothing Then
            For i = 0 To ids.Count - 1
                Dim id = ids(i)
                Dim camInfo = cameras.FirstOrDefault(Function(c) c.DeviceId = id)
                Dim lbl As String
                If camInfo IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(camInfo.Name) Then
                    lbl = $"相機 {i + 1} — {camInfo.Name}"
                Else
                    lbl = $"相機 {i + 1}"
                End If
                _combo.Items.Add(New CameraItem() With {.Label = lbl, .DeviceId = id, .Slot = i})
            Next
        End If

        If _combo.Items.Count > 0 Then _combo.SelectedIndex = 0

        panel.Children.Add(_combo)

        ' Buttons
        Dim btnRow As New StackPanel()
        btnRow.Orientation = Orientation.Horizontal
        btnRow.HorizontalAlignment = HorizontalAlignment.Right

        Dim btnCancel As New Button()
        btnCancel.Content = "取消"
        btnCancel.Width = 80
        btnCancel.Height = 30
        btnCancel.Foreground = Brushes.Black
        btnCancel.Background = New SolidColorBrush(Color.FromRgb(&HE8, &HE8, &HE8))
        btnCancel.BorderThickness = New Thickness(1)
        btnCancel.BorderBrush = New SolidColorBrush(Color.FromRgb(&HD0, &HD0, &HD0))
        btnCancel.Margin = New Thickness(0, 0, 8, 0)
        AddHandler btnCancel.Click, Sub(s, ev) Me.DialogResult = False
        btnRow.Children.Add(btnCancel)

        Dim btnOk As New Button()
        btnOk.Content = "確定"
        btnOk.Width = 80
        btnOk.Height = 30
        btnOk.Foreground = Brushes.White
        btnOk.Background = New SolidColorBrush(Color.FromRgb(&H0, &H78, &HD4))
        btnOk.BorderThickness = New Thickness(0)
        AddHandler btnOk.Click, AddressOf OkClicked
        btnRow.Children.Add(btnOk)

        panel.Children.Add(btnRow)
        Me.Content = panel
    End Sub

    Private Sub OkClicked(sender As Object, e As RoutedEventArgs)
        Dim item = TryCast(_combo.SelectedItem, CameraItem)
        If item IsNot Nothing Then
            SelectedCameraId = item.DeviceId
            SelectedCameraSlot = item.Slot
        End If
        Me.DialogResult = True
    End Sub

    Private Class CameraItem
        Public Property Label As String
        Public Property DeviceId As String
        Public Property Slot As Integer
    End Class

End Class
