Partial Public Class ErrorDialog

    Public Sub New()
        InitializeComponent()
    End Sub

    Public Sub New(message As String)
        InitializeComponent()
        TxtMessage.Text = message
    End Sub

    Private Sub BtnOk_Click(sender As Object, e As RoutedEventArgs)
        Close()
    End Sub

End Class


Public NotInheritable Class ErrorDialogHelper

    Public Shared Sub ShowError(message As String)

        Dim action = Sub()
                         Dim dlg As New ErrorDialog(message)
                         dlg.Owner = Application.Current.MainWindow
                         dlg.ShowDialog()
                     End Sub

        If Application.Current.Dispatcher.CheckAccess() Then
            action()
        Else
            Application.Current.Dispatcher.BeginInvoke(action)
        End If

    End Sub

End Class