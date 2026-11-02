import os
import subprocess
import tempfile

BASE_DIR = os.path.dirname(os.path.abspath(__file__))
CSC_PATH = r"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

CS_TEMPLATE = r"""
using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

static class Program {
    [STAThread]
    static void Main() {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string scriptPath = Path.Combine(baseDir, "__SCRIPT_NAME__");
        if (!File.Exists(scriptPath)) {
            MessageBox.Show("No se encontró el archivo: " + scriptPath, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        ProcessStartInfo psi = new ProcessStartInfo();
        psi.FileName = "pythonw.exe";
        psi.Arguments = "\"" + scriptPath + "\"";
        psi.WorkingDirectory = baseDir;
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;

        try {
            Process.Start(psi);
        } catch {
            try {
                psi.FileName = "python.exe";
                Process.Start(psi);
            } catch (Exception ex) {
                MessageBox.Show("No se pudo iniciar Python: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
"""


def build_exe(exe_name: str, script_name: str):
    cs_code = CS_TEMPLATE.replace("__SCRIPT_NAME__", script_name)
    out_exe = os.path.join(BASE_DIR, exe_name)
    with tempfile.NamedTemporaryFile(suffix=".cs", delete=False, mode="w", encoding="utf-8") as tmp:
        tmp.write(cs_code)
        tmp_path = tmp.name

    try:
        cmd = [
            CSC_PATH,
            "/nologo",
            "/target:winexe",
            f"/out:{out_exe}",
            "/r:System.Windows.Forms.dll",
            tmp_path,
        ]
        res = subprocess.run(cmd, capture_output=True, text=True)
        if res.returncode != 0:
            raise RuntimeError(res.stdout + "\n" + res.stderr)
        print(f"[OK] Ejecutable creado: {out_exe}")
    finally:
        if os.path.exists(tmp_path):
            os.remove(tmp_path)


if __name__ == "__main__":
    build_exe("Caja_POS.exe", "programa_caja.py")
    build_exe("Cocina_POS.exe", "programa_cocina.py")
