using Microsoft.Data.SqlClient;
using System;
using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using BCrypt.Net;

namespace InventoryManager
{
    public partial class UserManagerWindow : Window
    {
        private string _connectionString;
        private ObservableCollection<User> UsersList = new ObservableCollection<User>();
        private User? _selectedUser;

        public UserManagerWindow(string connectionString)
        {
            InitializeComponent();
            _connectionString = connectionString;
            UsersListView.ItemsSource = UsersList;
            LoadUsers();
        }

        private void LoadUsers()
        {
            UsersList.Clear();
            string query = "SELECT UserID, Username, FullName, Role, PasswordHash FROM Users;";
            using (var con = new SqlConnection(_connectionString))
            using (var cmd = new SqlCommand(query, con))
            {
                try
                {
                    con.Open();
                    var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        UsersList.Add(new User
                        {
                            UserID = reader.GetInt32(0),
                            Username = reader.GetString(1),
                            FullName = reader.GetString(2),
                            Role = reader.GetString(3),
                            PasswordHash = reader.GetString(4)
                        });
                    }
                }
                catch (Exception ex) { MessageBox.Show("Gagal memuat pengguna: " + ex.Message); }
            }
        }

        private void UsersListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedUser = UsersListView.SelectedItem as User;
            if (_selectedUser != null)
            {
                UserDetailPanel.IsEnabled = true;
                UsernameTextBox.Text = _selectedUser.Username;
                FullNameTextBox.Text = _selectedUser.FullName;
                RoleComboBox.Text = _selectedUser.Role;
                PasswordBox.Clear();
            }
            else
            {
                ClearForm();
                UserDetailPanel.IsEnabled = false;
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(UsernameTextBox.Text) || string.IsNullOrWhiteSpace(FullNameTextBox.Text))
            {
                MessageBox.Show("Username dan Nama Lengkap tidak boleh kosong.");
                return;
            }

            string query;
            if (_selectedUser == null) // Mode Tambah Baru
            {
                if (string.IsNullOrWhiteSpace(PasswordBox.Password))
                {
                    MessageBox.Show("Password tidak boleh kosong untuk pengguna baru.");
                    return;
                }
                query = "INSERT INTO Users (Username, FullName, Role, PasswordHash) VALUES (@Username, @FullName, @Role, @PasswordHash);";
            }
            else // Mode Edit
            {
                query = "UPDATE Users SET Username = @Username, FullName = @FullName, Role = @Role" +
                        (!string.IsNullOrWhiteSpace(PasswordBox.Password) ? ", PasswordHash = @PasswordHash" : "") +
                        " WHERE UserID = @UserID;";
            }

            using (var con = new SqlConnection(_connectionString))
            using (var cmd = new SqlCommand(query, con))
            {
                cmd.Parameters.AddWithValue("@Username", UsernameTextBox.Text);
                cmd.Parameters.AddWithValue("@FullName", FullNameTextBox.Text);
                cmd.Parameters.AddWithValue("@Role", (RoleComboBox.SelectedItem as ComboBoxItem)?.Content.ToString());
                if (!string.IsNullOrWhiteSpace(PasswordBox.Password))
                {
                    // PERBAIKAN: Gunakan nama lengkap untuk menghindari ambiguitas
                    string passwordHash = BCrypt.Net.BCrypt.HashPassword(PasswordBox.Password);
                    cmd.Parameters.AddWithValue("@PasswordHash", passwordHash);
                }

                if (_selectedUser != null)
                {
                    cmd.Parameters.AddWithValue("@UserID", _selectedUser.UserID);
                }

                try
                {
                    con.Open();
                    cmd.ExecuteNonQuery();
                    MessageBox.Show("Pengguna berhasil disimpan!");
                    LoadUsers();
                    ClearForm();
                }
                catch (Exception ex) { MessageBox.Show("Gagal menyimpan pengguna: " + ex.Message); }
            }
        }

        private void NewButton_Click(object sender, RoutedEventArgs e)
        {
            ClearForm();
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedUser == null) return;
            if (MessageBox.Show($"Yakin ingin menghapus pengguna '{_selectedUser.Username}'?", "Konfirmasi", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                string query = "DELETE FROM Users WHERE UserID = @UserID;";
                using (var con = new SqlConnection(_connectionString))
                using (var cmd = new SqlCommand(query, con))
                {
                    cmd.Parameters.AddWithValue("@UserID", _selectedUser.UserID);
                    try
                    {
                        con.Open();
                        cmd.ExecuteNonQuery();
                        MessageBox.Show("Pengguna berhasil dihapus.");
                        LoadUsers();
                        ClearForm();
                    }
                    catch (Exception ex) { MessageBox.Show("Gagal menghapus pengguna: " + ex.Message); }
                }
            }
        }

        private void ClearForm()
        {
            _selectedUser = null;
            UsersListView.SelectedItem = null;
            UserDetailPanel.IsEnabled = true;
            UsernameTextBox.Clear();
            FullNameTextBox.Clear();
            PasswordBox.Clear();
            RoleComboBox.SelectedIndex = -1;
        }
    }

    public class User
    {
        public int UserID { get; set; }
        public string Username { get; set; } = "";
        public string FullName { get; set; } = "";
        public string Role { get; set; } = "";
        public string PasswordHash { get; set; } = "";
    }
}
