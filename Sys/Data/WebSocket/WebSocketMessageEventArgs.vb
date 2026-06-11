Public Class WebSocketMessageEventArgs
    Inherits EventArgs

    Public Property Source As String
    Public Property Message As String

    Public Sub New(source As String, message As String)
        Me.Source = source
        Me.Message = message
    End Sub

End Class