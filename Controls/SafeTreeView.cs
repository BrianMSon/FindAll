using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;

namespace FindAll.Controls;

/// <summary>
/// TreeView that safely handles NullReferenceException during keyboard navigation
/// when items are being updated.
/// </summary>
public class SafeTreeView : TreeView
{
    // Use TreeView's control theme so SafeTreeView renders identically to TreeView
    protected override Type StyleKeyOverride => typeof(TreeView);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        try
        {
            base.OnKeyDown(e);
        }
        catch (NullReferenceException ex)
        {
            // This happens when TreeView tries to navigate while items are being updated
            Debug.WriteLine($"SafeTreeView: Caught NullReferenceException during navigation: {ex.Message}");
            e.Handled = true; // Suppress the error
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"SafeTreeView: Unexpected error during navigation: {ex.Message}");
            e.Handled = true;
        }
    }
}
