using Microsoft.Data.SqlClient;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace InventoryManager
{
    public partial class LinkProductWindow : Window, INotifyPropertyChanged
    {
        private readonly string _connectionString;
        private readonly int _originalProductId;
        private readonly string _originalProductName;

        public ObservableCollection<Product> AvailableProducts { get; set; }
        public ObservableCollection<Product> LinkedProducts { get; set; }
        public string WindowTitle { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;

        public LinkProductWindow(Product originalProduct, string connectionString)
        {
            InitializeComponent();
            DataContext = this;
            _originalProductId = originalProduct.ProductId;
            _originalProductName = originalProduct.ProductName;
            _connectionString = connectionString;

            WindowTitle = $"Tautkan Produk untuk: {_originalProductName}";

            AvailableProducts = new ObservableCollection<Product>();
            LinkedProducts = new ObservableCollection<Product>();

            LoadProducts();
        }

        private void LoadProducts()
        {
            AvailableProducts.Clear();
            LinkedProducts.Clear();

            var allProducts = new ObservableCollection<Product>();
            var linkedProductIds = new ObservableCollection<int>();

            try
            {
                using (var con = new SqlConnection(_connectionString))
                {
                    con.Open();
                    // Ambil semua produk
                    string allProductsQuery = "SELECT ProductID, ProductName FROM Products WHERE ProductID != @OriginalProductID ORDER BY ProductName;";
                    using (var cmd = new SqlCommand(allProductsQuery, con))
                    {
                        cmd.Parameters.AddWithValue("@OriginalProductID", _originalProductId);
                        var reader = cmd.ExecuteReader();
                        while (reader.Read())
                        {
                            allProducts.Add(new Product { ProductId = reader.GetInt32(0), ProductName = reader.GetString(1) });
                        }
                        reader.Close();
                    }

                    // Ambil produk yang sudah tertaut
                    string linkedProductsQuery = "SELECT LinkedProductID FROM ProductLinks WHERE OriginalProductID = @OriginalProductID;";
                    using (var cmd = new SqlCommand(linkedProductsQuery, con))
                    {
                        cmd.Parameters.AddWithValue("@OriginalProductID", _originalProductId);
                        var reader = cmd.ExecuteReader();
                        while (reader.Read())
                        {
                            linkedProductIds.Add(reader.GetInt32(0));
                        }
                    }
                }

                // Pisahkan produk ke daftar yang sesuai
                foreach (var product in allProducts)
                {
                    if (linkedProductIds.Contains(product.ProductId))
                    {
                        LinkedProducts.Add(product);
                    }
                    else
                    {
                        AvailableProducts.Add(product);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Gagal memuat data produk: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AddLinkButton_Click(object sender, RoutedEventArgs e)
        {
            if (AvailableProductsListView.SelectedItem is Product selectedProduct)
            {
                string query = "INSERT INTO ProductLinks (OriginalProductID, LinkedProductID) VALUES (@OriginalProductID, @LinkedProductID);";
                ExecuteLinkCommand(query, selectedProduct.ProductId);
                AvailableProducts.Remove(selectedProduct);
                LinkedProducts.Add(selectedProduct);
            }
        }

        private void RemoveLinkButton_Click(object sender, RoutedEventArgs e)
        {
            if (LinkedProductsListView.SelectedItem is Product selectedProduct)
            {
                string query = "DELETE FROM ProductLinks WHERE OriginalProductID = @OriginalProductID AND LinkedProductID = @LinkedProductID;";
                ExecuteLinkCommand(query, selectedProduct.ProductId);
                LinkedProducts.Remove(selectedProduct);
                AvailableProducts.Add(selectedProduct);
            }
        }

        private void ExecuteLinkCommand(string query, int targetProductId)
        {
            try
            {
                using (var con = new SqlConnection(_connectionString))
                using (var cmd = new SqlCommand(query, con))
                {
                    cmd.Parameters.AddWithValue("@OriginalProductID", _originalProductId);
                    cmd.Parameters.AddWithValue("@LinkedProductID", targetProductId);
                    con.Open();
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Gagal memperbarui tautan: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void AvailableProductsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            AddLinkButton.IsEnabled = AvailableProductsListView.SelectedItem != null;
        }

        private void LinkedProductsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RemoveLinkButton.IsEnabled = LinkedProductsListView.SelectedItem != null;
        }

        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
