using Microsoft.Web.WebView2.Core;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using WpfApp1.Utilities;

namespace WpfApp1
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            // Initialize WebView2 and load Monaco Editor HTML
            _ = InitializeWebViewAsync();
        }

        private async Task InitializeWebViewAsync()
        {
            // Ensure the WebView2 environment is created
            await WebView.EnsureCoreWebView2Async();

            // Minimal HTML that loads Monaco Editor from CDN
            string html = @"
<!doctype html>
<html>
<head>
  <meta charset='utf-8' />
  <meta http-equiv='Content-Security-Policy' content=""default-src 'self' https: data: 'unsafe-inline' 'unsafe-eval'"">
  <style>
    html, body, #container { height: 100%; margin: 0; padding: 0; }
  </style>
</head>
<body>
  <div id='container'></div>
  <script src='https://cdn.jsdelivr.net/npm/monaco-editor@0.41.0/min/vs/loader.js'></script>
  <script>
    require.config({ paths: { 'vs': 'https://cdn.jsdelivr.net/npm/monaco-editor@0.41.0/min/vs' }});
    require(['vs/editor/editor.main'], function() {
      monaco.editor.create(document.getElementById('container'), {
        value: [
          'function hello() {',
          '  console.log(\'xx\');',
          '}'
        ].join('\n'),
        language: 'javascript',
        theme: 'vs-dark',
        automaticLayout: true
      });
    });
  </script>
</body>
</html>
";
            WebView.NavigateToString(html);
        }

        private void BtnRun(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine(DateTime.Now);
            try
            {
                var token = new CancellationToken();
                var arr = new byte[2147483591];
                for (int j = 0; j < 5; j++) {
                    Debug.WriteLine(j + " " + DateTime.Now);                    
                    for (int i = 0; i < 2147483591; i++)
                        arr[i] = 0x21;
                    BinaryParallelWriter.WriteAsync("D:\\temp\\test.bin", arr, 0, 4194303, 0, true, token).Wait();
                }
            }catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
            Debug.WriteLine(DateTime.Now);
            Debug.WriteLine("Done");
        }
    }
}