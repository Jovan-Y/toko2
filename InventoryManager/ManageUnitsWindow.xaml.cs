using Microsoft.Data.SqlClient;
using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace InventoryManager
{
    public partial class ManageUnitsWindow : Window
    {
        private string _connectionString;
        private ObservableCollection<Unit> UnitsList = new ObservableCollection<Unit>();
        private Unit? _selectedUnit;

        public ManageUnitsWindow(string connectionString)
        {
            InitializeComponent();
            _connectionString = connectionString;
            UnitsListBox.ItemsSource = UnitsList;
            LoadUnits();
        }

        private void LoadUnits()
        {
            UnitsList.Clear();
            string query = "SELECT UnitID, UnitName, DefaultWarningThreshold FROM Units;";
            using (var con = new SqlConnection(_connectionString))
            using (var cmd = new SqlCommand(query, con))
            {
                try
                {
                    con.Open();
                    var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        UnitsList.Add(new Unit
                        {
                            UnitID = reader.GetInt32(0),
                            UnitName = reader.GetString(1),
                            DefaultWarningThreshold = reader.GetInt32(2)
                        });
                    }
                }
                catch (Exception ex) { MessageBox.Show("Gagal memuat satuan: " + ex.Message); }
            }
        }

        private void UnitsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedUnit = UnitsListBox.SelectedItem as Unit;
            if (_selectedUnit != null)
            {
                DetailPanel.IsEnabled = true;
                UnitNameTextBox.Text = _selectedUnit.UnitName;
                DefaultThresholdTextBox.Text = _selectedUnit.DefaultWarningThreshold.ToString();
            }
            else
            {
                ClearForm();
                DetailPanel.IsEnabled = false;
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(DefaultThresholdTextBox.Text, out int threshold))
            {
                MessageBox.Show("Default peringatan harus berupa angka.");
                return;
            }

            string query;
            if (_selectedUnit == null) // Mode Tambah Baru
            {
                query = "INSERT INTO Units (UnitName, DefaultWarningThreshold) VALUES (@Name, @Threshold);";
            }
            else // Mode Edit
            {
                query = "UPDATE Units SET UnitName = @Name, DefaultWarningThreshold = @Threshold WHERE UnitID = @ID;";
            }

            using (var con = new SqlConnection(_connectionString))
            using (var cmd = new SqlCommand(query, con))
            {
                cmd.Parameters.AddWithValue("@Name", UnitNameTextBox.Text);
                cmd.Parameters.AddWithValue("@Threshold", threshold);
                if (_selectedUnit != null)
                {
                    cmd.Parameters.AddWithValue("@ID", _selectedUnit.UnitID);
                }

                try
                {
                    con.Open();
                    cmd.ExecuteNonQuery();
                    MessageBox.Show("Satuan berhasil disimpan!");
                    LoadUnits();
                }
                catch (Exception ex) { MessageBox.Show("Gagal menyimpan satuan: " + ex.Message); }
            }
        }

        private void NewButton_Click(object sender, RoutedEventArgs e)
        {
            ClearForm();
        }

        private void ClearForm()
        {
            _selectedUnit = null;
            UnitsListBox.SelectedItem = null;
            DetailPanel.IsEnabled = true;
            UnitNameTextBox.Clear();
            DefaultThresholdTextBox.Text = "5"; // Default
            UnitNameTextBox.Focus();
        }
    }
}
