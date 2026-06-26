Public Module LogCenter

    Public Event LogAdded(msg As String)

    Public Sub Write(msg As String)

        RaiseEvent LogAdded(msg)

    End Sub

End Module