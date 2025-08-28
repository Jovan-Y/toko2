using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Configuration;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace InventoryManager
{
    public partial class ManageBundleWindow : Window
    {
        private readonly string _connectionString = ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;
        private List<Product> _allAvailableProducts = new List<Product>();
        public ObservableCollection<Product> AvailableProducts { get; set; }
        public ObservableCollection<BundleComponent> LinkedProducts { get; set; }

        public ManageBundleWindow()
        {
            InitializeComponent();
            DataContext = this;
            AvailableProducts = new ObservableCollection<Product>();
            LinkedProducts = new ObservableCollection<BundleComponent>();
            LoadAvailableProducts();
        }

        private void LoadAvailableProducts()
        {
            _allAvailableProducts.Clear();
            string query = "SELECT ProductID, ProductName FROM Products WHERE IsBundle = 0 ORDER BY ProductName;";
            using (var con = new SqlConnection(_connectionString))
            using (var cmd = new SqlCommand(query, con))
            {
                try
                {
                    con.Open();
                    var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        _allAvailableProducts.Add(new Product { ProductId = reader.GetInt32(0), ProductName = reader.GetString(1) });
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Gagal memuat produk: " + ex.Message);
                }
            }
            FilterAvailableProducts("");
        }

        private void FilterAvailableProducts(string searchTerm)
        {
            AvailableProducts.Clear();
            var filtered = _allAvailableProducts.Where(p => p.ProductName.ToLower().Contains(searchTerm.ToLower()));
            foreach (var item in filtered)
            {
                AvailableProducts.Add(item);
            }
        }

        private void SearchComponentTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            FilterAvailableProducts(SearchComponentTextBox.Text);
        }

        private void AddLinkButton_Click(object sender, RoutedEventArgs e)
        {
            if (AvailableProductsListView.SelectedItem is Product selectedProduct)
            {
                if (!LinkedProducts.Any(p => p.ProductId == selectedProduct.ProductId))
                {
                    LinkedProducts.Add(new BundleComponent { ProductId = selectedProduct.ProductId, ProductName = selectedProduct.ProductName, Quantity = 1 });
                }
            }
        }

        private void RemoveLinkButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is BundleComponent itemToRemove)
            {
                LinkedProducts.Remove(itemToRemove);
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(BundleNameTextBox.Text) || !decimal.TryParse(BundlePriceTextBox.Text, out decimal price) || !LinkedProducts.Any())
            {
                MessageBox.Show("Nama, harga jual, dan minimal satu komponen harus diisi.", "Input Tidak Valid", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            using (var con = new SqlConnection(_connectionString))
            {
                con.Open();
                var transaction = con.BeginTransaction();
                try
                {
                    // 1. Buat produk bundle baru
                    string insertBundleQuery = @"
                        INSERT INTO Products (ProductName, IsBundle, Stock) 
                        OUTPUT INSERTED.ProductID 
                        VALUES (@ProductName, 1, 0);";
                    int newBundleId;
                    using (var cmd = new SqlCommand(insertBundleQuery, con, transaction))
                    {
                        cmd.Parameters.AddWithValue("@ProductName", BundleNameTextBox.Text);
                        newBundleId = (int)cmd.ExecuteScalar();
                    }

                    // 2. Tambahkan satu-satunya unit konversi untuk bundle itu sendiri
                    string insertConversionQuery = @"
                        INSERT INTO ProductUnitConversions (ProductID, UnitID, ConversionFactor, SellingPrice, Barcode)
                        VALUES (@ProductID, (SELECT UnitID FROM Units WHERE UnitName='Pcs'), 1, @SellingPrice, @Barcode);";
                    using (var cmd = new SqlCommand(insertConversionQuery, con, transaction))
                    {
                        cmd.Parameters.AddWithValue("@ProductID", newBundleId);
                        cmd.Parameters.AddWithValue("@SellingPrice", price);
                        cmd.Parameters.AddWithValue("@Barcode", string.IsNullOrWhiteSpace(BundleBarcodeTextBox.Text) ? (object)DBNull.Value : BundleBarcodeTextBox.Text);
                        cmd.ExecuteNonQuery();
                    }

                    // 3. Tautkan semua komponen
                    foreach (var component in LinkedProducts)
                    {
                        string insertLinkQuery = @"
                            INSERT INTO ProductLinks (BundleProductID, ComponentProductID, QuantityPerBundle)
                            VALUES (@BundleID, @ComponentID, @Quantity);";
                        using (var cmd = new SqlCommand(insertLinkQuery, con, transaction))
                        {
                            cmd.Parameters.AddWithValue("@BundleID", newBundleId);
                            cmd.Parameters.AddWithValue("@ComponentID", component.ProductId);
                            cmd.Parameters.AddWithValue("@Quantity", component.Quantity);
                            cmd.ExecuteNonQuery();
                        }
                    }

                    transaction.Commit();
                    MessageBox.Show("Bundle berhasil disimpan!", "Sukses", MessageBoxButton.OK, MessageBoxImage.Information);
                    this.DialogResult = true;
                    this.Close();
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    MessageBox.Show("Gagal menyimpan bundle: " + ex.Message, "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }

    public class BundleComponent
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; } = "";
        public int Quantity { get; set; }
    }
}
