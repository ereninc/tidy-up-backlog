Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class CozyUnityMenu {
    [DllImport("user32.dll")] static extern IntPtr GetMenu(IntPtr window);
    [DllImport("user32.dll")] static extern IntPtr GetSubMenu(IntPtr menu, int index);
    [DllImport("user32.dll")] static extern int GetMenuItemCount(IntPtr menu);
    [DllImport("user32.dll")] static extern uint GetMenuItemID(IntPtr menu, int index);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetMenuString(IntPtr menu, uint index, StringBuilder text, int length, uint flags);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
    static uint Find(IntPtr menu, string parent, StringBuilder report) {
        for (int i=0;i<GetMenuItemCount(menu);i++) {
            var text=new StringBuilder(512);
            GetMenuString(menu,(uint)i,text,512,0x400);
            string name=text.ToString().Replace("&", "").Split('\t')[0];
            string path=parent+"/"+name;
            uint id=GetMenuItemID(menu,i);
            report.AppendLine(path+"="+id);
            if(path=="/Assets/Refresh") return id;
            IntPtr child=GetSubMenu(menu,i);
            if(child!=IntPtr.Zero) {
                uint found=Find(child,path,report);
                if(found!=0) return found;
            }
        }
        return 0;
    }
    public static string Refresh(IntPtr window) {
        var report=new StringBuilder();
        uint id=Find(GetMenu(window),"",report);
        if(id==0) return "Refresh not found.\n"+report;
        return "Unity Assets/Refresh command="+id+" posted="+PostMessage(window,0x111,(IntPtr)id,IntPtr.Zero);
    }
}
'@
$unityEditor = Get-Process -Id 27676
if ($unityEditor.ProcessName -ne 'Unity') { throw 'Wrong process.' }
[CozyUnityMenu]::Refresh($unityEditor.MainWindowHandle)


