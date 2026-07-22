Imports System.Windows
Imports System.Windows.Input
Imports System.Windows.Media

Public Class MeteorMessageBox
    Public Property Result As MessageBoxResult = MessageBoxResult.None

    Public Sub New(message As String, title As String, button As MessageBoxButton, icon As MessageBoxImage)
        InitializeComponent()
        
        TitleBlock.Text = title
        MessageBlock.Text = message
        
        ' Icon Setup
        Select Case icon
            Case MessageBoxImage.Error, MessageBoxImage.Stop, MessageBoxImage.Hand
                IconText.Text = ChrW(&HEA39) ' ErrorBadge
                IconText.Foreground = New SolidColorBrush(Color.FromRgb(&HF2, &H8B, &H82))
                IconText.Visibility = Visibility.Visible
            Case MessageBoxImage.Warning, MessageBoxImage.Exclamation
                IconText.Text = ChrW(&HE7BA) ' Warning
                IconText.Foreground = New SolidColorBrush(Color.FromRgb(&HFD, &HE2, &H93))
                IconText.Visibility = Visibility.Visible
            Case MessageBoxImage.Information, MessageBoxImage.Asterisk
                IconText.Text = ChrW(&HE946) ' Info
                IconText.Foreground = New SolidColorBrush(Color.FromRgb(&H8A, &HB4, &HF8))
                IconText.Visibility = Visibility.Visible
            Case MessageBoxImage.Question
                IconText.Text = ChrW(&HE897) ' Help
                IconText.Foreground = New SolidColorBrush(Color.FromRgb(&H8A, &HB4, &HF8))
                IconText.Visibility = Visibility.Visible
        End Select

        ' Button Setup
        Select Case button
            Case MessageBoxButton.OK
                BtnOk.Visibility = Visibility.Visible
            Case MessageBoxButton.OKCancel
                BtnOk.Visibility = Visibility.Visible
                BtnCancel.Visibility = Visibility.Visible
            Case MessageBoxButton.YesNo
                BtnYes.Visibility = Visibility.Visible
                BtnNo.Visibility = Visibility.Visible
            Case MessageBoxButton.YesNoCancel
                BtnYes.Visibility = Visibility.Visible
                BtnNo.Visibility = Visibility.Visible
                BtnCancel.Visibility = Visibility.Visible
        End Select
    End Sub

    Private Sub Header_MouseLeftButtonDown(sender As Object, e As MouseButtonEventArgs)
        If e.LeftButton = MouseButtonState.Pressed Then
            DragMove()
        End If
    End Sub

    Private Sub BtnOk_Click(sender As Object, e As RoutedEventArgs)
        Result = MessageBoxResult.OK
        Close()
    End Sub

    Private Sub BtnCancel_Click(sender As Object, e As RoutedEventArgs)
        Result = MessageBoxResult.Cancel
        Close()
    End Sub

    Private Sub BtnYes_Click(sender As Object, e As RoutedEventArgs)
        Result = MessageBoxResult.Yes
        Close()
    End Sub

    Private Sub BtnNo_Click(sender As Object, e As RoutedEventArgs)
        Result = MessageBoxResult.No
        Close()
    End Sub

    ' The generic static Show method
    Public Shared Function Show(message As String, Optional title As String = "提示", Optional button As MessageBoxButton = MessageBoxButton.OK, Optional icon As MessageBoxImage = MessageBoxImage.None) As MessageBoxResult
        Dim dispatcher = Application.Current.Dispatcher
        If dispatcher.CheckAccess() Then
            Dim dlg As New MeteorMessageBox(message, title, button, icon)
            ' Ensure we don't set Owner to a window     that hasn't been fully loaded yet or is closing
            If Application.Current.MainWindow IsNot Nothing AndAlso Application.Current.MainWindow.IsLoaded Then
                Try
                    dlg.Owner = Application.Current.MainWindow
                Catch
                End Try
            End If
            dlg.ShowDialog()
            Return dlg.Result
        Else
            Return dispatcher.Invoke(Function() Show(message, title, button, icon))
        End If
    End Function

    Public Shared Sub ShowError(message As String)
        Show(message, "錯誤", MessageBoxButton.OK, MessageBoxImage.Error)
    End Sub
End Class
