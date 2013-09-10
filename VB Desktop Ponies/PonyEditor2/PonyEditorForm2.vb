﻿Imports System.IO

Public Class PonyEditorForm2
    Private ReadOnly worker As IdleWorker = IdleWorker.CurrentThreadWorker
    Private ReadOnly bases As New Dictionary(Of String, MutablePonyBase)()
    Private ReadOnly nodeLookup As New Dictionary(Of String, TreeNode)()

    Private workingCount As Integer

    Private Class PageRef
        Private ReadOnly _ponyBase As MutablePonyBase
        Public ReadOnly Property PonyBase As MutablePonyBase
            Get
                Return _ponyBase
            End Get
        End Property
        Private ReadOnly _pageContent As PageContent
        Public ReadOnly Property PageContent As PageContent
            Get
                Return _pageContent
            End Get
        End Property
        Public Property Item As IPonyIniSourceable
        Public Sub New(ponyBase As MutablePonyBase, pageContent As PageContent, item As IPonyIniSourceable)
            _ponyBase = ponyBase
            _pageContent = pageContent
            Me.Item = item
        End Sub
        Public Overrides Function ToString() As String
            Return String.Join(Path.DirectorySeparatorChar,
                               If(PonyBase IsNot Nothing, PonyBase.Directory, ""),
                               PageContent,
                               If(Item IsNot Nothing, Item.Name, Nothing))
        End Function
    End Class

    Private ReadOnly Property ActiveItemEditor As ItemEditorBase
        Get
            Return If(Documents.SelectedTab Is Nothing, Nothing, DirectCast(Documents.SelectedTab.Controls(0), ItemEditorBase))
        End Get
    End Property

    Public Sub New(ponyBaseCollection As IEnumerable(Of PonyBase))
        InitializeComponent()
        Icon = My.Resources.Twilight
        DocumentsView.PathSeparator = Path.DirectorySeparatorChar
    End Sub

    Private Sub PonyEditorForm2_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        Enabled = False
        Dim screenArea = Screen.FromHandle(Handle).WorkingArea.Size
        Size = New Size(CInt(screenArea.Width * 0.8), screenArea.Height)
        CenterToScreen()
        worker.QueueTask(Sub()
                             Dim images = New ImageList()
                             images.Images.Add(SystemIcons.Warning)
                             images.Images.Add(SystemIcons.WinLogo)
                             images.Images.Add(SystemIcons.Error)
                             DocumentsView.ImageList = images
                         End Sub)
        Threading.ThreadPool.QueueUserWorkItem(Sub() LoadBases())
    End Sub

    Private Sub LoadBases()
        Dim poniesNode As TreeNode = Nothing
        worker.QueueTask(Sub()
                             poniesNode = New TreeNode("Ponies") With
                                          {.Tag = New PageRef(Nothing, PageContent.Ponies, Nothing)}
                             DocumentsView.Nodes.Add(poniesNode)
                             nodeLookup(poniesNode.Name) = poniesNode
                             poniesNode.Expand()
                         End Sub)

        PonyCollection.LoadAll(
            Sub(count) worker.QueueTask(Sub() EditorProgressBar.Maximum = count),
            Sub(pony) worker.QueueTask(
                Sub()
                    bases.Add(pony.Directory, pony)
                    Dim ponyBaseRef = New PageRef(pony, PageContent.Pony, Nothing)
                    Dim ponyBaseNode = New TreeNode(pony.Directory) With
                                       {.Tag = ponyBaseRef, .Name = ponyBaseRef.ToString()}
                    poniesNode.Nodes.Add(ponyBaseNode)
                    nodeLookup(ponyBaseNode.Name) = ponyBaseNode

                    Dim behaviorsRef = New PageRef(pony, PageContent.Behaviors, Nothing)
                    Dim behaviorsNode = New TreeNode("Behaviors") With
                                       {.Tag = behaviorsRef, .Name = behaviorsRef.ToString()}
                    ponyBaseNode.Nodes.Add(behaviorsNode)
                    nodeLookup(behaviorsNode.Name) = behaviorsNode
                    Dim effectsRef = New PageRef(pony, PageContent.Effects, Nothing)
                    Dim effectsNode = New TreeNode("Effects") With
                                          {.Tag = effectsRef, .Name = effectsRef.ToString()}
                    ponyBaseNode.Nodes.Add(effectsNode)
                    nodeLookup(effectsNode.Name) = effectsNode
                    Dim speechesRef = New PageRef(pony, PageContent.Speeches, Nothing)
                    Dim speechesNode = New TreeNode("Speeches") With
                                       {.Tag = speechesRef, .Name = speechesRef.ToString()}
                    ponyBaseNode.Nodes.Add(speechesNode)
                    nodeLookup(speechesNode.Name) = speechesNode

                    For Each behavior In pony.Behaviors
                        Dim ref = New PageRef(pony, PageContent.Behavior, behavior)
                        Dim node = New TreeNode(behavior.Name) With {.Tag = ref, .Name = ref.ToString()}
                        behaviorsNode.Nodes.Add(node)
                        nodeLookup(node.Name) = node
                    Next

                    For Each effect In pony.Effects
                        Dim ref = New PageRef(pony, PageContent.Effect, effect)
                        Dim node = New TreeNode(effect.Name) With {.Tag = ref, .Name = ref.ToString()}
                        effectsNode.Nodes.Add(node)
                        nodeLookup(node.Name) = node
                    Next

                    For Each speech In pony.Speeches
                        Dim ref = New PageRef(pony, PageContent.Speech, speech)
                        Dim node = New TreeNode(speech.Name) With {.Tag = ref, .Name = ref.ToString()}
                        speechesNode.Nodes.Add(node)
                        nodeLookup(node.Name) = node
                    Next

                    EditorProgressBar.Value += 1
                End Sub))
        worker.QueueTask(Sub()
                             poniesNode.TreeView.Sort()
                         End Sub)

        worker.QueueTask(Sub()
                             Dim basesCopy = bases.Values.ToArray()
                             For Each base In bases.Values
                                 base.LoadInteractions(basesCopy)
                             Next
                         End Sub)
        worker.QueueTask(Sub()
                             EditorStatus.Text = "Ready"
                             EditorProgressBar.Value = 1
                             EditorProgressBar.Maximum = 1
                             EditorProgressBar.Style = ProgressBarStyle.Marquee
                             EditorProgressBar.Visible = False
                             DocumentsView.TopNode.Expand()
                             UseWaitCursor = False
                             Enabled = True
                             DocumentsView.Focus()
                         End Sub)
        worker.WaitOnAllTasks()
        ValidateBases()
    End Sub

    Private Sub ValidateBases()
        For Each base In bases.Values.OrderBy(Function(pb) pb.Directory)
            Dim behaviorsError = False
            For Each behavior In base.Behaviors
                Dim ref = New PageRef(base, PageContent.Behavior, behavior)
                Dim parseIssues As ParseIssue() = Nothing
                Dim b As Behavior = Nothing
                Dim behaviorError = Not behavior.TryLoad(
                    behavior.SourceIni,
                    Path.Combine(Options.InstallLocation, PonyBase.RootDirectory, ref.PonyBase.Directory),
                    ref.PonyBase, b, parseIssues) OrElse b.GetReferentialIssues().Length > 0
                behaviorsError = behaviorsError OrElse behaviorError
                worker.QueueTask(Sub()
                                     Dim node = FindNode(ref.ToString())
                                     node.ImageIndex = If(behaviorError, 2, 1)
                                 End Sub)
            Next
            worker.QueueTask(Sub()
                                 Dim ref = New PageRef(base, PageContent.Behaviors, Nothing)
                                 Dim node = FindNode(ref.ToString())
                                 node.ImageIndex = If(behaviorsError, 2, 1)
                             End Sub)
            Dim effectsError = False
            For Each effect In base.Effects
                Dim ref = New PageRef(base, PageContent.Effect, effect)
                Dim parseIssues As ParseIssue() = Nothing
                Dim e As EffectBase = Nothing
                Dim effectError = Not EffectBase.TryLoad(
                    effect.SourceIni,
                    Path.Combine(Options.InstallLocation, PonyBase.RootDirectory, ref.PonyBase.Directory),
                    ref.PonyBase, e, parseIssues) OrElse e.GetReferentialIssues().Length > 0
                effectsError = effectsError OrElse effectError
                worker.QueueTask(Sub()
                                     Dim node = FindNode(ref.ToString())
                                     node.ImageIndex = If(effectError, 2, 1)
                                 End Sub)
            Next
            worker.QueueTask(Sub()
                                 Dim ref = New PageRef(base, PageContent.Effects, Nothing)
                                 Dim node = FindNode(ref.ToString())
                                 node.ImageIndex = If(effectsError, 2, 1)
                             End Sub)

            Dim speechesError = False
            For Each speech In base.Speeches
                Dim ref = New PageRef(base, PageContent.Speech, speech)
                Dim parseIssues As ParseIssue() = Nothing
                Dim s As Speech = Nothing
                Dim speechError = Not speech.TryLoad(
                    speech.SourceIni,
                    Path.Combine(Options.InstallLocation, PonyBase.RootDirectory, ref.PonyBase.Directory),
                    speech, parseIssues)
                speechesError = speechesError OrElse speechError
                worker.QueueTask(Sub()
                                     Dim node = FindNode(ref.ToString())
                                     node.ImageIndex = If(speechError, 2, 1)
                                 End Sub)
            Next
            worker.QueueTask(Sub()
                                 Dim ref = New PageRef(base, PageContent.Speeches, Nothing)
                                 Dim node = FindNode(ref.ToString())
                                 node.ImageIndex = If(speechesError, 2, 1)
                             End Sub)

            worker.QueueTask(Sub()
                                 Dim ref = New PageRef(base, PageContent.Pony, Nothing)
                                 Dim node = FindNode(ref.ToString())
                                 node.ImageIndex = If(behaviorsError OrElse effectsError OrElse speechesError, 2, 1)
                             End Sub)
        Next
        worker.WaitOnAllTasks()
    End Sub

    Private Function FindNode(name As String) As TreeNode
        Dim node As TreeNode = Nothing
        If nodeLookup.TryGetValue(name, node) Then
            Return node
        Else
            Return DocumentsView.Nodes.Find(name, True).Single()
        End If
    End Function

    Private Shared Function GetTabText(pageRef As PageRef) As String
        Select Case pageRef.PageContent
            Case PageContent.Ponies
                Return PageContent.Ponies.ToString()
            Case PageContent.Pony
                Return pageRef.PonyBase.Directory
            Case PageContent.Behaviors, PageContent.Effects, PageContent.Speeches
                Return pageRef.PonyBase.Directory & " - " & pageRef.PageContent.ToString()
            Case PageContent.Behavior, PageContent.Effect, PageContent.Speech
                Return pageRef.PonyBase.Directory & ": " & pageRef.Item.Name
            Case Else
                Throw New System.ComponentModel.InvalidEnumArgumentException("Unknown Content in pageRef")
        End Select
    End Function

    Private Shadows Function GetPageRef(tab As TabPage) As PageRef
        Return DirectCast(tab.Tag, PageRef)
    End Function

    Private Shadows Function GetPageRef(node As TreeNode) As PageRef
        Return DirectCast(node.Tag, PageRef)
    End Function

    Private Sub DocumentsView_NodeMouseDoubleClick(sender As Object, e As TreeNodeMouseClickEventArgs) Handles DocumentsView.NodeMouseDoubleClick
        OpenTab(GetPageRef(e.Node))
    End Sub

    Private Sub DocumentsView_KeyPress(sender As Object, e As KeyPressEventArgs) Handles DocumentsView.KeyPress
        If e.KeyChar = ChrW(Keys.Enter) Then
            e.Handled = OpenTab(GetPageRef(DocumentsView.SelectedNode))
        End If
    End Sub

    Private Function OpenTab(pageRef As PageRef) As Boolean
        Dim pageRefKey = pageRef.ToString()
        Dim tab = Documents.TabPages.Item(pageRefKey)

        If tab Is Nothing Then
            Dim editor As ItemEditorBase = Nothing
            Select Case pageRef.PageContent
                Case PageContent.Behavior
                    editor = New BehaviorEditor()
                Case PageContent.Effect
                    editor = New EffectEditor()
                Case PageContent.Speech
                    editor = New SpeechEditor()
            End Select
            If editor IsNot Nothing Then
                QueueWorkItem(Sub() editor.LoadItem(pageRef.PonyBase, pageRef.Item))
                editor.Dock = DockStyle.Fill
                tab = New ItemTabPage() With {.Name = pageRefKey, .Text = GetTabText(pageRef), .Tag = pageRef}
                tab.Controls.Add(editor)
                Documents.TabPages.Add(tab)
                CloseTabButton.Enabled = True
                CloseAllTabsButton.Enabled = True
            End If
        End If

        If tab IsNot Nothing Then
            Documents.SelectedTab = tab
            SwitchTab(tab)
            DocumentsView.Select()
            DocumentsView.SelectedNode = DocumentsView.Nodes.Find(pageRefKey, True)(0)
            Return True
        End If

        Return False
    End Function

    Private Sub Documents_Selected(sender As Object, e As TabControlEventArgs) Handles Documents.Selected
        SwitchTab(e.TabPage)
    End Sub

    Private Sub SwitchTab(newTab As TabPage)
        If Documents.SelectedTab IsNot Nothing Then
            RemoveHandler ActiveItemEditor.IssuesChanged, AddressOf ActiveItemEditor_IssuesChanged
            RemoveHandler ActiveItemEditor.DirtinessChanged, AddressOf ActiveItemEditor_DirtinessChanged
            ActiveItemEditor.AnimateImages(False)
        End If

        Documents.SelectedTab = newTab

        If Documents.SelectedTab IsNot Nothing Then
            ActiveItemEditor.AnimateImages(True)
            AddHandler ActiveItemEditor.DirtinessChanged, AddressOf ActiveItemEditor_DirtinessChanged
            AddHandler ActiveItemEditor.IssuesChanged, AddressOf ActiveItemEditor_IssuesChanged
        End If
        ActiveItemEditor_DirtinessChanged(Me, EventArgs.Empty)
        ActiveItemEditor_IssuesChanged(Me, EventArgs.Empty)
    End Sub

    Private Sub ActiveItemEditor_DirtinessChanged(sender As Object, e As EventArgs)
        Dim dirty = If(ActiveItemEditor Is Nothing, False, ActiveItemEditor.IsItemDirty)
        SaveItemButton.Enabled = dirty
        SaveItemButton.ToolTipText = If(dirty,
                                        "Save the changes made to the item in the visible tab.",
                                        "No changes have been made to the item in the visible tab.")
    End Sub

    Private Sub ActiveItemEditor_IssuesChanged(sender As Object, e As EventArgs)
        IssuesGrid.SuspendLayout()
        IssuesGrid.Rows.Clear()
        If ActiveItemEditor IsNot Nothing Then
            For Each issue In ActiveItemEditor.Issues
                IssuesGrid.Rows.Add(If(issue.Fatal, SystemIcons.Error, SystemIcons.Warning),
                                    If(issue.PropertyName, "Element " & issue.Index + 1),
                                    issue.Reason,
                                    issue.FallbackValue,
                                    issue.Source)
            Next
        End If
        IssuesGrid.ResumeLayout()
    End Sub

    Private Sub SaveButton_Click(sender As Object, e As EventArgs) Handles SaveItemButton.Click
        ActiveItemEditor.SaveItem()
        Dim ref = GetPageRef(Documents.SelectedTab)
        Dim node = DocumentsView.Nodes.Find(ref.ToString(), True)(0)

        ref.Item = ActiveItemEditor.Item
        Documents.SelectedTab.Text = GetTabText(ref)
        node.Name = ref.ToString()
        node.Text = ref.Item.Name

        EditorStatus.Text = "Saved"
    End Sub

    Private Sub CloseTabButton_Click(sender As Object, e As EventArgs) Handles CloseTabButton.Click
        RemoveTab(Documents.SelectedTab)
        SwitchTab(Documents.SelectedTab)
    End Sub

    Private Sub CloseAllTabsButton_Click(sender As Object, e As EventArgs) Handles CloseAllTabsButton.Click
        For Each t In Documents.TabPages.Cast(Of TabPage)().ToArray()
            RemoveTab(t)
        Next
        SwitchTab(Documents.SelectedTab)
    End Sub

    Private Sub RemoveTab(tab As TabPage)
        Documents.TabPages.Remove(tab)
        tab.Dispose()
        CloseTabButton.Enabled = Documents.TabPages.Count > 0
        CloseAllTabsButton.Enabled = Documents.TabPages.Count > 0
    End Sub

    Private Sub QueueWorkItem(item As MethodInvoker)
        workingCount += 1
        EditorProgressBar.Visible = True
        EditorStatus.Text = "Working..."
        worker.QueueTask(Sub()
                             Try
                                 item()
                             Finally
                                 workingCount -= 1
                                 If workingCount = 0 Then
                                     EditorProgressBar.Visible = False
                                     EditorStatus.Text = "Ready"
                                 End If
                             End Try
                         End Sub)
    End Sub

    Private Sub PonyEditorForm2_FormClosing(sender As Object, e As FormClosingEventArgs) Handles MyBase.FormClosing
        ' TODO: Change IdleWorker to allow it be depend on a specified control, and drop remaining tasks if the control is destroyed.
        ' Until then, we'll make sure we process any tasks before closing to prevent errors.
        worker.WaitOnAllTasks()
    End Sub
End Class