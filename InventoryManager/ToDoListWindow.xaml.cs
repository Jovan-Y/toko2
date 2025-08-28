using Microsoft.Data.SqlClient;
using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace InventoryManager
{
    public class ToDoItem
    {
        public int ToDoID { get; set; }
        public string ProductName { get; set; }
        public string TaskType { get; set; }
        public string Description { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public partial class ToDoListWindow : Window
    {
        private string _connectionString;
        private ObservableCollection<ToDoItem> _toDoItems = new ObservableCollection<ToDoItem>();

        public ToDoListWindow(string connectionString)
        {
            InitializeComponent();
            _connectionString = connectionString;
            ToDoListView.ItemsSource = _toDoItems;
            Loaded += (s, e) => LoadToDoItems();
        }

        private void LoadToDoItems()
        {
            _toDoItems.Clear();
            string query = @"
                SELECT t.ToDoID, p.ProductName, t.TaskType, t.Description, t.CreatedAt
                FROM ToDoItems t
                JOIN Products p ON t.ProductID = p.ProductID
                WHERE t.IsCompleted = 0
                ORDER BY t.CreatedAt DESC;";

            using (var con = new SqlConnection(_connectionString))
            using (var cmd = new SqlCommand(query, con))
            {
                try
                {
                    con.Open();
                    var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        _toDoItems.Add(new ToDoItem
                        {
                            ToDoID = reader.GetInt32(0),
                            ProductName = reader.GetString(1),
                            TaskType = reader.GetString(2),
                            Description = reader.GetString(3),
                            CreatedAt = reader.GetDateTime(4)
                        });
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Gagal memuat daftar tugas: " + ex.Message);
                }
            }
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            LoadToDoItems();
        }

        private void CompleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.CommandParameter is int toDoId)
            {
                string query = "UPDATE ToDoItems SET IsCompleted = 1 WHERE ToDoID = @ToDoID";
                using (var con = new SqlConnection(_connectionString))
                using (var cmd = new SqlCommand(query, con))
                {
                    cmd.Parameters.AddWithValue("@ToDoID", toDoId);
                    try
                    {
                        con.Open();
                        cmd.ExecuteNonQuery();
                        LoadToDoItems(); // Refresh list setelah update
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Gagal menyelesaikan tugas: " + ex.Message);
                    }
                }
            }
        }
    }
}
