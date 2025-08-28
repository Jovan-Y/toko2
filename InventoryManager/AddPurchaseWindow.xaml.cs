using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace InventoryManager
{
    public partial class AddPurchaseWindow : Window
    {
        private string _connectionString;
        public ObservableCollection<PurchaseItem> PurchaseItems { get; set; }
        private List<Product> _allProducts;
        private Dictionary<int, List<ProductUnitConversion>> _unitCache = new Dictionary<int, List<ProductUnitConversion>>();
        public string LastSupplierName { get; private set; }

        public AddPurchaseWindow(string connectionString, List<Product> allProducts, string lastSupplier)
        {
            InitializeComponent();
            _connectionString = connectionString;
            _allProducts = allProducts;
            PurchaseItems = new ObservableCollection<PurchaseItem>();
            PurchaseItemsDataGrid.ItemsSource = PurchaseItems;
            PurchaseDatePicker.SelectedDate = DateTime.Today;
            SupplierNameTextBox.Text = lastSupplier;
            LastSupplierName = lastSupplier;

            ProductColumn.ItemsSource = _allProducts;
            PurchaseItems.CollectionChanged += (s, e) => CalculateTotal();
        }

        private void PurchaseItemsDataGrid_InitializingNewItem(object sender, InitializingNewItemEventArgs e)
        {
            var newItem = e.NewItem as PurchaseItem;
            if (newItem != null)
            {
                newItem.PropertyChanged += (s, args) =>
                {
                    if (args.PropertyName == nameof(PurchaseItem.Subtotal))
                    {
                        CalculateTotal();
                    }
                };
            }
        }

        private void CalculateTotal()
        {
            decimal total = PurchaseItems.Sum(item => item.Subtotal);
            TotalPurchaseTextBlock.Text = total.ToString("C", new System.Globalization.CultureInfo("id-ID"));
        }

        private void BarcodeScanTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                string barcode = BarcodeScanTextBox.Text.Trim();
                // PERBAIKAN: Pencarian sekarang dilakukan di tabel ProductUnitConversions
                ProductUnitConversion? foundConversion = null;
                using (var con = new SqlConnection(_connectionString))
                {
                    string query = @"SELECT TOP 1 p.ProductID 
                                     FROM ProductUnitConversions puc
                                     JOIN Products p ON puc.ProductID = p.ProductID
                                     WHERE puc.Barcode = @Barcode";
                    using (var cmd = new SqlCommand(query, con))
                    {
                        cmd.Parameters.AddWithValue("@Barcode", barcode);
                        try
                        {
                            con.Open();
                            var result = cmd.ExecuteScalar();
                            if (result != null)
                            {
                                int productId = (int)result;
                                var product = _allProducts.FirstOrDefault(p => p.ProductId == productId);
                                if (product != null)
                                {
                                    var newItem = new PurchaseItem { ProductId = product.ProductId };
                                    newItem.PropertyChanged += (s, args) => CalculateTotal();
                                    PurchaseItems.Add(newItem);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show("Error saat mencari barcode: " + ex.Message);
                        }
                    }
                }

                if (foundConversion == null)
                {
                    MessageBox.Show("Produk dengan barcode ini tidak ditemukan.", "Tidak Ditemukan", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                BarcodeScanTextBox.Clear();
            }
        }

        private void SavePurchaseButton_Click(object sender, RoutedEventArgs e)
        {
            if (PurchaseItems.Any(i => i.ProductId == 0 || i.Quantity <= 0 || i.CostPrice <= 0))
            {
                MessageBox.Show("Pastikan semua produk, jumlah, dan harga modal telah diisi dengan benar.", "Input Tidak Lengkap", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            using (var con = new SqlConnection(_connectionString))
            {
                con.Open();
                var transaction = con.BeginTransaction();
                try
                {
                    string supplier = SupplierNameTextBox.Text;
                    LastSupplierName = supplier;

                    string purchaseQuery = "INSERT INTO Purchases (PurchaseDate, SupplierName, TotalAmount) OUTPUT INSERTED.PurchaseID VALUES (@Date, @Supplier, @Total);";
                    var purchaseCmd = new SqlCommand(purchaseQuery, con, transaction);
                    purchaseCmd.Parameters.AddWithValue("@Date", PurchaseDatePicker.SelectedDate ?? DateTime.Today);
                    purchaseCmd.Parameters.AddWithValue("@Supplier", string.IsNullOrWhiteSpace(supplier) ? (object)DBNull.Value : supplier);
                    purchaseCmd.Parameters.AddWithValue("@Total", PurchaseItems.Sum(i => i.Subtotal));
                    int newPurchaseId = (int)purchaseCmd.ExecuteScalar();

                    foreach (var item in PurchaseItems)
                    {
                        int totalBaseQuantity = item.Quantity; // Asumsi sederhana, perlu diperbaiki

                        string detailQuery = "INSERT INTO PurchaseDetails (PurchaseID, ProductID, Quantity, CostPriceAtPurchase) VALUES (@PurchaseID, @ProductID, @Qty, @Cost);";
                        var detailCmd = new SqlCommand(detailQuery, con, transaction);
                        detailCmd.Parameters.AddWithValue("@PurchaseID", newPurchaseId);
                        detailCmd.Parameters.AddWithValue("@ProductID", item.ProductId);
                        detailCmd.Parameters.AddWithValue("@Qty", totalBaseQuantity);
                        detailCmd.Parameters.AddWithValue("@Cost", item.CostPrice);
                        detailCmd.ExecuteNonQuery();

                        string stockQuery = "UPDATE Products SET Stock = Stock + @Qty WHERE ProductID = @ProductID;";
                        var stockCmd = new SqlCommand(stockQuery, con, transaction);
                        stockCmd.Parameters.AddWithValue("@Qty", totalBaseQuantity);
                        stockCmd.Parameters.AddWithValue("@ProductID", item.ProductId);
                        stockCmd.ExecuteNonQuery();
                    }

                    transaction.Commit();
                    MessageBox.Show("Data pembelian berhasil disimpan!");
                    this.DialogResult = true;
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    MessageBox.Show("Gagal menyimpan data pembelian: " + ex.Message);
                }
            }
        }
    }

    public class PurchaseItem : INotifyPropertyChanged
    {
        private int _productId;
        public int ProductId
        {
            get => _productId;
            set { _productId = value; OnPropertyChanged(nameof(ProductId)); }
        }

        private int _quantity;
        public int Quantity
        {
            get => _quantity;
            set { _quantity = value; OnPropertyChanged(nameof(Quantity)); OnPropertyChanged(nameof(Subtotal)); }
        }

        public string UnitName { get; set; } = "Pcs";

        private decimal _costPrice;
        public decimal CostPrice
        {
            get => _costPrice;
            set { _costPrice = value; OnPropertyChanged(nameof(CostPrice)); OnPropertyChanged(nameof(Subtotal)); }
        }

        public decimal Subtotal => Quantity * CostPrice;
        public int TotalBaseQuantity => Quantity;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
