using System;
using System.Configuration;
using System.Data;
using Microsoft.Data.SqlClient;
using System.Windows;

namespace InventoryManager
{
    public partial class AddProductWindow : Window
    {
        private readonly string connectionString = ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;

        public AddProductWindow()
        {
            InitializeComponent();
            ProductNameTextBox.Focus();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(ProductNameTextBox.Text))
            {
                MessageBox.Show("Nama produk tidak boleh kosong.", "Validasi Gagal", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            string newProductName = ProductNameTextBox.Text;

            string alternateNames = AlternateNamesTextBox.Text;

            decimal? costPrice = null;
            if (!string.IsNullOrWhiteSpace(CostPriceTextBox.Text))
            {
                if (!decimal.TryParse(CostPriceTextBox.Text, out decimal parsedCostPrice) || parsedCostPrice < 0)
                {
                    MessageBox.Show("Jika diisi, harga awal harus berupa angka positif.", "Validasi Gagal", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                costPrice = parsedCostPrice;
            }

            using (SqlConnection checkCon = new SqlConnection(connectionString))
            {
                try
                {
                    checkCon.Open();
                    string checkQuery = "SELECT COUNT(1) FROM Products WHERE ProductName = @ProductName";
                    using (SqlCommand checkCmd = new SqlCommand(checkQuery, checkCon))
                    {
                        checkCmd.Parameters.AddWithValue("@ProductName", newProductName);
                        int existingCount = (int)checkCmd.ExecuteScalar();
                        if (existingCount > 0)
                        {
                            MessageBox.Show("Nama produk sudah ada. Silakan gunakan nama lain.", "Duplikat Ditemukan", MessageBoxButton.OK, MessageBoxImage.Error);
                            return;
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Gagal memvalidasi nama produk. Error: " + ex.Message, "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }

            using (SqlConnection con = new SqlConnection(connectionString))
            {
                try
                {
                    con.Open();
                    string query = "INSERT INTO Products (ProductName, CostPrice, Stock, WarningStockLevel, AlternateNames) VALUES (@ProductName, @CostPrice, @Stock, @WarningStockLevel, @AlternateNames)";

                    using (SqlCommand cmd = new SqlCommand(query, con))
                    {
                        cmd.CommandType = CommandType.Text;
                        cmd.Parameters.AddWithValue("@ProductName", newProductName);
                        cmd.Parameters.AddWithValue("@CostPrice", costPrice.HasValue ? (object)costPrice.Value : DBNull.Value);
                        cmd.Parameters.AddWithValue("@Stock", 0);
                        cmd.Parameters.AddWithValue("@WarningStockLevel", 0);
                        cmd.Parameters.AddWithValue("@AlternateNames", string.IsNullOrWhiteSpace(alternateNames) ? (object)DBNull.Value : alternateNames);

                        cmd.ExecuteNonQuery();

                        MessageBox.Show("Produk berhasil ditambahkan!", "Sukses", MessageBoxButton.OK, MessageBoxImage.Information);

                        this.DialogResult = true;
                        this.Close();
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Gagal menyimpan produk ke database. Error: " + ex.Message, "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}
