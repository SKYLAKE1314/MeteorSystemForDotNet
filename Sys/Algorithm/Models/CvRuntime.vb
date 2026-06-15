Public Module CvRuntime

    Public Property CurrentTemplateName As String

    Public Sub ApplyTemplate(templatePath As String)

        If String.IsNullOrWhiteSpace(templatePath) Then Return

        ' 1. 存 Last
        LastTemplateStore.Save(templatePath)

        ' 2. 更新 runtime state
        CurrentTemplateName = IO.Path.GetFileName(templatePath)

        Logger.Info($"模板已切換: {CurrentTemplateName}")

    End Sub

End Module