DevUtils
========

For simple tasks like checking the output of the C++ preprocessor or the code generation Visual Studio requires you to:
- go to the file or project properties and set the correct compiler flags and output options
- run the compiler
- locate the generated file
- find your function in the hundreds of thousands of LoCs

This is an extension that automates those tedious steps via convenient context menu entries.

It is alpha quality but does its job.
Should be used together with an assembler syntax highlighting extension like AsmHighlighter.

Download
--------
https://visualstudiogallery.msdn.microsoft.com/62f485b0-b659-4852-8f39-885c20c9fcd1

Known issues
------------

- Doesn't work with headers.
- Currently uses Release|x64 hard-coded.
- It does undo the changes to the compile options afterwards but the project file will still have a new empty entry for that file added if you save it.
- https://connect.microsoft.com/VisualStudio/feedback/details/809115/vcfileconfiguration-compile-only-works-with-the-currently-active-configuration


TODO
----
- cleanup!
- better methods for finding the correct spot in the generated file
- prevent pollution of project file by finding a working undo mechanism
- /showIncludes tree view
- RunToCursor functionality with ignoring breakpoints


    Dim bptStates(DTE.Debugger.Breakpoints.Count - 1) As Boolean


    Dim i = 0
    For Each bpt As Breakpoint In DTE.Debugger.Breakpoints
        bptStates(i) = bpt.Enabled
        i += 1
        bpt.Enabled = False
    Next

    Try
        DTE.Debugger.RunToCursor(True)
        '       Catch ex As Exception
    Finally
        i = 0
        For Each bpt As Breakpoint In DTE.Debugger.Breakpoints
            bpt.Enabled = bptStates(i)
            i += 1
        Next
    End Try
	
	
	But for some reason the RunToCursor call doesn't work at all ("Operation not supported") and breakpoint states aren't properly reset either. Some are reactivated but not all. Any ideas?