using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using System.Text.RegularExpressions;
// avoid direct reference to WinForms; use reflection fallback for folder browser

namespace sqleditor
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private ObservableCollection<UserRow> _rows = new ObservableCollection<UserRow>();
        private string _selectedFolder = null;
        private bool _editingExistingFile = false;
        private string _editingFilePath = null;

        public MainWindow()
        {
            InitializeComponent();
            // set default SQL type if available
            var cmb = this.FindName("CmbSqlType") as ComboBox;
            if (cmb != null) cmb.SelectedIndex = 0;

            // initialize with 3 empty rows
            for (int i = 0; i < 3; i++)
                _rows.Add(new UserRow());

            var dg = this.FindName("DataGridUsers") as DataGrid;
            if (dg != null) dg.ItemsSource = _rows;
        }

        private void BtnAddRow_Click(object sender, RoutedEventArgs e)
        {
            _rows.Add(new UserRow());
        }

        private void BtnEditSql_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog();
            dlg.Filter = "SQL files (*.sql)|*.sql|All files (*.*)|*.*";
            if (!string.IsNullOrWhiteSpace(_selectedFolder) && Directory.Exists(_selectedFolder))
                dlg.InitialDirectory = _selectedFolder;

            var res = dlg.ShowDialog();
            if (res == true)
            {
                var path = dlg.FileName;
                try
                {
                    var text = File.ReadAllText(path, Encoding.UTF8);
                    ParseAndLoadSql(text);
                    _editingExistingFile = true;
                    _editingFilePath = path;

                    // set folder and project name hints
                    var fiForFolder = new FileInfo(path);
                    _selectedFolder = fiForFolder.DirectoryName;
                    var txt = this.FindName("TxtFolderPath") as TextBlock;
                    if (txt != null) txt.Text = _selectedFolder;
                    var proj = this.FindName("TxtProjectName") as TextBox;
                    if (proj != null)
                    {
                        var fi = new FileInfo(path);
                        var nameOnly = fi.Name;
                        if (!string.IsNullOrEmpty(fi.Extension) && nameOnly.EndsWith(fi.Extension))
                            nameOnly = nameOnly.Substring(0, nameOnly.Length - fi.Extension.Length);
                        proj.Text = nameOnly;
                    }

                    // change Export button to Save
                    var btn = this.FindName("BtnExport") as Button;
                    if (btn != null) btn.Content = "Save";
                    System.Windows.MessageBox.Show("SQL file loaded for editing.", "Loaded", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (System.Exception ex)
                {
                    System.Windows.MessageBox.Show("Failed to open SQL file: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnChooseFolder_Click(object sender, RoutedEventArgs e)
        {
            // Try to open WinForms FolderBrowserDialog via reflection (optional). If not available, fall back to manual input window.
            try
            {
                var fbType = System.Type.GetType("System.Windows.Forms.FolderBrowserDialog, System.Windows.Forms");
                if (fbType != null)
                {
                    var fb = System.Activator.CreateInstance(fbType);
                    try
                    {
                        var descProp = fbType.GetProperty("Description");
                        var showNewProp = fbType.GetProperty("ShowNewFolderButton");
                        descProp?.SetValue(fb, "Choose or create a folder for the project");
                        showNewProp?.SetValue(fb, true);

                        var showMethod = fbType.GetMethod("ShowDialog", new System.Type[] { });
                        var res = showMethod.Invoke(fb, null);
                        // DialogResult.OK == 1
                        if (res != null && (int)res == 1)
                        {
                            var selPathProp = fbType.GetProperty("SelectedPath");
                            var path = selPathProp?.GetValue(fb) as string;
                            if (!string.IsNullOrWhiteSpace(path))
                            {
                                _selectedFolder = path;
                                var txt = this.FindName("TxtFolderPath") as TextBlock;
                                if (txt != null) txt.Text = _selectedFolder;
                                return;
                            }
                        }
                    }
                    finally
                    {
                        // dispose if possible
                        var disp = fb as System.IDisposable;
                        disp?.Dispose();
                    }
                }
            }
            catch { /* ignore and fall back */ }

            // fallback: simple input window to let user paste or type a folder path
            var w = new Window()
            {
                Title = "Select or create folder",
                Width = 520,
                Height = 140,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this
            };

            var panel = new StackPanel() { Margin = new Thickness(8) };
            var tb = new TextBox() { Text = _selectedFolder ?? string.Empty };
            var btns = new StackPanel() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,8,0,0) };
            var ok = new Button() { Content = "OK", Width = 80, IsDefault = true, Margin = new Thickness(4,0,0,0) };
            var cancel = new Button() { Content = "Cancel", Width = 80, IsCancel = true, Margin = new Thickness(4,0,0,0) };
            ok.Click += (s, ev) => { w.DialogResult = true; w.Close(); };
            cancel.Click += (s, ev) => { w.DialogResult = false; w.Close(); };
            btns.Children.Add(ok);
            btns.Children.Add(cancel);
            panel.Children.Add(new TextBlock() { Text = "Type or paste folder path (will be created if not exists):" });
            panel.Children.Add(tb);
            panel.Children.Add(btns);
            w.Content = panel;
            var dialogRes = w.ShowDialog();
            if (dialogRes == true)
            {
                var path = tb.Text.Trim();
                            if (!string.IsNullOrWhiteSpace(path))
                            {
                                try
                                {
                                    if (!Directory.Exists(path)) Directory.CreateDirectory(path);
                                    _selectedFolder = path;
                                    var txt = this.FindName("TxtFolderPath") as TextBlock;
                                    if (txt != null) txt.Text = _selectedFolder;
                                }
                                catch (System.Exception ex)
                                {
                                    System.Windows.MessageBox.Show("Could not create or access folder: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                                }
                            }
            }
        }

        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            // if currently editing an existing SQL file, save back to that file
            if (_editingExistingFile && !string.IsNullOrWhiteSpace(_editingFilePath))
            {
                SaveExistingFile();
                return;
            }
            if (string.IsNullOrWhiteSpace(_selectedFolder))
            {
                System.Windows.MessageBox.Show("Please choose a folder first.", "Folder required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var projectName = (this.FindName("TxtProjectName") as TextBox)?.Text?.Trim();
            if (string.IsNullOrWhiteSpace(projectName))
            {
                System.Windows.MessageBox.Show("Please enter a project name.", "Project name required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var cmbItem = (this.FindName("CmbSqlType") as ComboBox)?.SelectedItem as ComboBoxItem;
            var dialect = cmbItem?.Content?.ToString() ?? "MySql";

            var sql = GenerateSql(dialect, _rows.ToList());

            try
            {
                var outPath = System.IO.Path.Combine(_selectedFolder, projectName + ".sql");
                File.WriteAllText(outPath, sql, Encoding.UTF8);
                System.Windows.MessageBox.Show($"Exported SQL to:\n{outPath}", "Export successful", MessageBoxButton.OK, MessageBoxImage.Information);
                ShowSqlWindow(sql);
            }
            catch (System.Exception ex)
            {
                System.Windows.MessageBox.Show("Failed to write SQL file: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ShowSqlWindow(string sql)
        {
            var w = new Window()
            {
                Title = "Generated SQL",
                Width = 800,
                Height = 600
            };

            var tb = new TextBox()
            {
                Text = sql,
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12
            };

            w.Content = tb;
            w.Owner = this;
            w.ShowDialog();
        }

        private void SaveExistingFile()
        {
            if (string.IsNullOrWhiteSpace(_editingFilePath)) return;
            var cmbItem = (this.FindName("CmbSqlType") as ComboBox)?.SelectedItem as ComboBoxItem;
            var dialect = cmbItem?.Content?.ToString() ?? "MySql";
            var sql = GenerateSql(dialect, _rows.ToList());
            try
            {
                File.WriteAllText(_editingFilePath, sql, Encoding.UTF8);
                System.Windows.MessageBox.Show($"Saved SQL to:\n{_editingFilePath}", "Save successful", MessageBoxButton.OK, MessageBoxImage.Information);
                // reset export button text
                var btn = this.FindName("BtnExport") as Button;
                if (btn != null) btn.Content = "Export";
                _editingExistingFile = false;
                _editingFilePath = null;
            }
            catch (System.Exception ex)
            {
                System.Windows.MessageBox.Show("Failed to save SQL file: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ParseAndLoadSql(string sqlText)
        {
            // detect dialect and set combo
            var dialect = DetectDialectFromSql(sqlText);
            var cmb = this.FindName("CmbSqlType") as ComboBox;
            if (cmb != null)
            {
                for (int i = 0; i < cmb.Items.Count; i++)
                {
                    var item = cmb.Items[i] as ComboBoxItem;
                    if (item != null && string.Equals(item.Content?.ToString(), dialect, System.StringComparison.OrdinalIgnoreCase))
                    {
                        cmb.SelectedIndex = i;
                        break;
                    }
                }
            }

            // parse INSERT INTO users (...) VALUES (...),(...); blocks
            _rows.Clear();
            try
            {
                // find VALUES (...) blocks after INSERT INTO users
                var insertIdx = sqlText.IndexOf("INSERT INTO users", System.StringComparison.OrdinalIgnoreCase);
                if (insertIdx >= 0)
                {
                    var semicolonIdx = sqlText.IndexOf(';', insertIdx);
                    var insertBlock = semicolonIdx > insertIdx ? sqlText.Substring(insertIdx, semicolonIdx - insertIdx) : sqlText.Substring(insertIdx);

                    // find tuples: parentheses groups
                    var tupleMatches = Regex.Matches(insertBlock, "\\(([^)]*)\\)");
                    foreach (Match m in tupleMatches)
                    {
                        var content = m.Groups[1].Value;
                        // extract quoted strings
                        var strMatches = Regex.Matches(content, "'((?:[^']|'')*)'");
                        if (strMatches.Count >= 3)
                        {
                            var username = strMatches.Count > 0 ? strMatches[0].Groups[1].Value.Replace("''", "'") : null;
                            var email = strMatches.Count > 1 ? strMatches[1].Groups[1].Value.Replace("''", "'") : null;
                            var password = strMatches.Count > 2 ? strMatches[2].Groups[1].Value.Replace("''", "'") : null;
                            var phone = strMatches.Count > 3 ? strMatches[3].Groups[1].Value.Replace("''", "'") : null;
                            var living = strMatches.Count > 4 ? strMatches[4].Groups[1].Value.Replace("''", "'") : null;
                            _rows.Add(new UserRow() { Username = username, Email = email, Password = password, PhoneNumber = phone, LivingPlace = living });
                        }
                    }
                }
            }
            catch { }

            if (_rows.Count == 0)
            {
                for (int i = 0; i < 3; i++) _rows.Add(new UserRow());
            }

            var dg = this.FindName("DataGridUsers") as DataGrid;
            if (dg != null) dg.ItemsSource = _rows;
        }

        private string DetectDialectFromSql(string sqlText)
        {
            if (string.IsNullOrWhiteSpace(sqlText)) return "MySql";
            var t = sqlText.ToUpperInvariant();
            if (t.Contains("AUTO_INCREMENT") || t.Contains("BOOLEAN DEFAULT FALSE")) return "MySql";
            if (t.Contains("SERIAL") || t.Contains("TIMESTAMP") && t.Contains("DEFAULT CURRENT_TIMESTAMP")) return "PostgreSQL";
            if (t.Contains("AUTOINCREMENT") || t.Contains("INTEGER PRIMARY KEY")) return "SQLite";
            return "MySql";
        }

        private string GenerateSql(string dialect, System.Collections.Generic.List<UserRow> rows)
        {
            var sb = new StringBuilder();

            // CREATE TABLE
            if (dialect == "MySql")
            {
                sb.AppendLine("CREATE TABLE IF NOT EXISTS users (");
                sb.AppendLine("    id INT AUTO_INCREMENT PRIMARY KEY,");
                sb.AppendLine("    username VARCHAR(50) NOT NULL UNIQUE,");
                sb.AppendLine("    email VARCHAR(100) NOT NULL UNIQUE,");
                sb.AppendLine("    password VARCHAR(255) NOT NULL,");
                sb.AppendLine("    phonenumber VARCHAR(20),");
                sb.AppendLine("    living_place VARCHAR(100),");
                sb.AppendLine("    is_premium BOOLEAN DEFAULT FALSE,");
                sb.AppendLine("    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP");
                sb.AppendLine(" -- Created with sqleditor app & made in Rhest corp. ");
                sb.AppendLine(");");
            }
            else if (dialect == "PostgreSQL")
            {
                sb.AppendLine("CREATE TABLE IF NOT EXISTS users (");
                sb.AppendLine("    id SERIAL PRIMARY KEY,");
                sb.AppendLine("    username VARCHAR(50) NOT NULL UNIQUE,");
                sb.AppendLine("    email VARCHAR(100) NOT NULL UNIQUE,");
                sb.AppendLine("    password VARCHAR(255) NOT NULL,");
                sb.AppendLine("    phonenumber VARCHAR(20),");
                sb.AppendLine("    living_place VARCHAR(100),");
                sb.AppendLine("    is_premium BOOLEAN DEFAULT FALSE,");
                sb.AppendLine("    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP");
                sb.AppendLine(" -- Created with sqleditor app & made in Rhest corp. ");
                sb.AppendLine(");");
            }
            else // SQLite
            {
                sb.AppendLine("CREATE TABLE IF NOT EXISTS users (");
                sb.AppendLine("    id INTEGER PRIMARY KEY AUTOINCREMENT,");
                sb.AppendLine("    username TEXT NOT NULL UNIQUE,");
                sb.AppendLine("    email TEXT NOT NULL UNIQUE,");
                sb.AppendLine("    password TEXT NOT NULL,");
                sb.AppendLine("    phonenumber TEXT,");
                sb.AppendLine("    living_place TEXT,");
                sb.AppendLine("    is_premium INTEGER DEFAULT 0,");
                sb.AppendLine("    created_at DATETIME DEFAULT CURRENT_TIMESTAMP");
                sb.AppendLine(" -- Created with sqleditor app & made in Rhest corp. ");
                sb.AppendLine(");");
            }

            sb.AppendLine();

            // INSERT statements
            var validRows = rows;
            if (validRows.Count > 0)
            {
                sb.AppendLine("INSERT INTO users (username, email, password, phonenumber, living_place, is_premium)");
                sb.AppendLine("VALUES ");
                var inserts = new System.Collections.Generic.List<string>();
                foreach (var r in validRows)
                {
                    string username = SqlEscape(r.Username);
                    string email = SqlEscape(r.Email);
                    string password = SqlEscape(r.Password);
                    string phone = string.IsNullOrWhiteSpace(r.PhoneNumber) ? "NULL" : "'" + SqlEscape(r.PhoneNumber) + "'";
                    string living = string.IsNullOrWhiteSpace(r.LivingPlace) ? "NULL" : "'" + SqlEscape(r.LivingPlace) + "'";

                    string isPremium = "FALSE";
                    // default false; the UI does not have a premium column so keep FALSE

                    var line = $"('{username}', '{email}', '{password}', {phone}, {living}, {isPremium})";
                    inserts.Add(line);
                }

                sb.AppendLine(string.Join(",\n", inserts) + ";");
                sb.AppendLine();
            }

            sb.AppendLine("SELECT * FROM users;");

            return sb.ToString();
        }

        private string SqlEscape(string input)
        {
            if (input == null) return "";
            return input.Replace("'", "''");
        }

        private class UserRow
        {
            public string Username { get; set; }
            public string Email { get; set; }
            public string Password { get; set; }
            public string PhoneNumber { get; set; }
            public string LivingPlace { get; set; }
        }

        private void DataGridUsers_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }
    }
}