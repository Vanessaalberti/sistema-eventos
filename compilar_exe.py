import os
import subprocess

BASE_DIR = os.path.dirname(os.path.abspath(__file__))
CSC_PATH = r"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
WPF_DIR = r"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF"


def compile_wpf_exe(exe_name: str, entry_cs: str):
    out_exe = os.path.join(BASE_DIR, exe_name)
    core_cs = os.path.join(BASE_DIR, "src", "PosCore.cs")
    icons_cs = os.path.join(BASE_DIR, "src", "LucideIcons.cs")
    main_cs = os.path.join(BASE_DIR, "src", entry_cs)

    cmd = [
        CSC_PATH,
        "/nologo",
        "/target:winexe",
        "/optimize+",
        f"/out:{out_exe}",
        f"/lib:{WPF_DIR}",
        "/r:PresentationFramework.dll",
        "/r:PresentationCore.dll",
        "/r:WindowsBase.dll",
        "/r:System.Xaml.dll",
        "/r:System.Drawing.dll",
        "/r:System.Printing.dll",
        "/r:System.Web.Extensions.dll",
        core_cs,
        icons_cs,
        main_cs,
    ]
    res = subprocess.run(cmd, capture_output=True, text=True)
    if res.returncode != 0:
        raise RuntimeError(f"Error compilando {exe_name}:\n{res.stdout}\n{res.stderr}")
    print(f"[OK] Ejecutable WPF nativo compilado: {out_exe}")


if __name__ == "__main__":
    compile_wpf_exe("Caja_POS.exe", "CajaWpf.cs")
    compile_wpf_exe("Cocina_POS.exe", "CocinaWpf.cs")
