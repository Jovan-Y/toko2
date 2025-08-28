using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace InventoryManager
{
    public partial class MainWindow : Window
    {
        private readonly string connectionString = ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;

        private ObservableCollection<Product> ProductsList = new ObservableCollection<Product>();
        private List<Product> _allProducts = new List<Product>();
        private ObservableCollection<ProductUnitConversion> ConversionsList = new ObservableCollection<ProductUnitConversion>();
        private ObservableCollection<Unit> AllUnitsList = new ObservableCollection<Unit>();

        private Product? _selectedProduct;

        public MainWindow()
        {
            InitializeComponent();

            ProductsListView.ItemsSource = ProductsList;
            ConversionsDataGrid.ItemsSource = ConversionsList;
            UnitsComboBox.ItemsSource = AllUnitsList;
            AddStockUnitComboBox.ItemsSource = ConversionsList;
            WarningStockUnitComboBox.ItemsSource = AllUnitsList;

            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LoadAllUnits();
            LoadAllProducts();
            CheckForLowStockItems();
            UpdateActiveSectionHighlight(ProductDetailBorder);
        }

        #region Data Loading Methods

        public void LoadAllProducts(bool preserveSearch = false)
        {
            string currentSearchTerm = preserveSearch ? SearchTextBox.Text : "";
            int? selectedProductId = preserveSearch ? (ProductsListView.SelectedItem as Product)?.ProductId : null;

            _allProducts.Clear();
            string query = @"
                SELECT p.ProductID, p.ProductName, p.Stock, p.CostPrice, 
                       p.WarningStockLevel, p.WarningStockUnitID, p.AlternateNames, p.IsBundle,
                       CASE WHEN EXISTS (SELECT 1 FROM ToDoItems t WHERE t.ProductID = p.ProductID AND t.IsCompleted = 0) THEN 1 ELSE 0 END AS HasToDo
                FROM Products p
                ORDER BY p.ProductName;";

            using (var con = new SqlConnection(connectionString))
            using (var cmd = new SqlCommand(query, con))
            {
                try
                {
                    con.Open();
                    var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        var product = new Product
                        {
                            ProductId = reader.GetInt32(0),
                            ProductName = reader.GetString(1),
                            Stock = reader.GetInt32(2),
                            CostPrice = reader.IsDBNull(3) ? (decimal?)null : reader.GetDecimal(3),
                            WarningStockLevel = reader.GetInt32(4),
                            WarningStockUnitID = reader.IsDBNull(5) ? 0 : reader.GetInt32(5),
                            AlternateNames = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                            IsBundle = reader.GetBoolean(7),
                            HasToDo = Convert.ToBoolean(reader["HasToDo"])
                        };
                        _allProducts.Add(product);
                    }
                }
                catch (Exception ex) { MessageBox.Show("Gagal memuat produk: " + ex.Message); }
            }

            LoadAllComponents();
            RecalculateBundleStocks();
            FilterProducts(currentSearchTerm);
            UpdateAllLowStockStatus();

            if (selectedProductId.HasValue)
            {
                var productToReselect = ProductsList.FirstOrDefault(p => p.ProductId == selectedProductId.Value);
                if (productToReselect != null)
                {
                    ProductsListView.SelectedItem = productToReselect;
                }
            }
        }

        private void LoadAllComponents()
        {
            foreach (var product in _allProducts)
            {
                product.Components.Clear();
            }

            string query = @"SELECT pl.BundleProductID, p.ProductName, pl.QuantityPerBundle, pl.ComponentProductID, pl.LinkID
                             FROM ProductLinks pl
                             JOIN Products p ON pl.ComponentProductID = p.ProductID";

            using (var con = new SqlConnection(connectionString))
            using (var cmd = new SqlCommand(query, con))
            {
                con.Open();
                var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    int bundleId = reader.GetInt32(0);
                    var bundleProduct = _allProducts.FirstOrDefault(p => p.ProductId == bundleId);
                    if (bundleProduct != null)
                    {
                        bundleProduct.Components.Add(new ProductLink
                        {
                            ComponentName = reader.GetString(1),
                            QuantityPerBundle = reader.GetDecimal(2),
                            ComponentProductId = reader.GetInt32(3),
                            LinkId = reader.GetInt32(4)
                        });
                    }
                }
            }
        }

        private void RecalculateBundleStocks()
        {
            foreach (var bundle in _allProducts.Where(p => p.IsBundle))
            {
                bundle.RecalculateStock(_allProducts);
            }
        }

        private void LoadAllUnits()
        {
            AllUnitsList.Clear();
            string query = "SELECT UnitID, UnitName, DefaultWarningThreshold FROM Units;";
            using (var con = new SqlConnection(connectionString))
            using (var cmd = new SqlCommand(query, con))
            {
                try
                {
                    con.Open();
                    var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        AllUnitsList.Add(new Unit
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

        private void LoadConversionsForProduct(int productId)
        {
            ConversionsList.Clear();
            string query = @"SELECT c.ConversionID, u.UnitName, c.ConversionFactor, c.SellingPrice, c.UnitID, c.CostPrice, c.Barcode, c.IsStockOnly 
                             FROM ProductUnitConversions c
                             JOIN Units u ON c.UnitID = u.UnitID
                             WHERE c.ProductID = @ProductID;";

            using (var con = new SqlConnection(connectionString))
            using (var cmd = new SqlCommand(query, con))
            {
                cmd.Parameters.AddWithValue("@ProductID", productId);
                try
                {
                    con.Open();
                    var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        ConversionsList.Add(new ProductUnitConversion
                        {
                            ConversionID = reader.GetInt32(0),
                            UnitName = reader.GetString(1),
                            ConversionFactor = reader.GetDecimal(2),
                            SellingPrice = reader.IsDBNull(3) ? 0 : reader.GetDecimal(3),
                            UnitID = reader.GetInt32(4),
                            CostPrice = reader.IsDBNull(5) ? (decimal?)null : reader.GetDecimal(5),
                            Barcode = reader.IsDBNull(6) ? "" : reader.GetString(6),
                            IsStockOnly = reader.GetBoolean(7)
                        });
                    }
                }
                catch (Exception ex) { MessageBox.Show("Gagal memuat konversi satuan: " + ex.Message); }
            }
        }

        #endregion

        #region Event Handlers (UI)

        private void ProductsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedProduct = ProductsListView.SelectedItem as Product;
            ClearUnitInputFields();

            if (_selectedProduct != null)
            {
                ProductDetailPanel.IsEnabled = true;
                ProductNameTextBox.Text = _selectedProduct.ProductName;
                AlternateNamesTextBox.Text = _selectedProduct.AlternateNames;
                CostPriceTextBox.Text = _selectedProduct.CostPrice?.ToString();
                LoadConversionsForProduct(_selectedProduct.ProductId);

                if (_selectedProduct.IsBundle)
                {
                    StockBreakdownTextBlock.Text = $"Stok Virtual: {_selectedProduct.Stock}";
                    AddStockButton.IsEnabled = false;
                }
                else
                {
                    StockBreakdownTextBlock.Text = CalculateStockBreakdown(_selectedProduct.Stock, ConversionsList.ToList());
                    AddStockButton.IsEnabled = true;
                }

                WarningStockLevelTextBox.Text = _selectedProduct.WarningStockLevel.ToString();
                WarningStockUnitComboBox.SelectedValue = _selectedProduct.WarningStockUnitID;
            }
            else
            {
                ProductDetailPanel.IsEnabled = false;
                ProductNameTextBox.Clear();
                AlternateNamesTextBox.Clear();
                CostPriceTextBox.Clear();
                StockBreakdownTextBlock.Text = "-";
                WarningStockLevelTextBox.Clear();
                ConversionsList.Clear();
            }
        }

        private void ManageBundleButton_Click(object sender, RoutedEventArgs e)
        {
            ManageBundleWindow manageBundleWindow = new ManageBundleWindow();
            manageBundleWindow.Owner = this;
            if (manageBundleWindow.ShowDialog() == true)
            {
                LoadAllProducts();
            }
        }

        private void ConversionsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ConversionsDataGrid.SelectedItem is ProductUnitConversion selectedConversion)
            {
                UnitsComboBox.SelectedValue = selectedConversion.UnitID;
                FactorTextBox.Text = selectedConversion.ConversionFactor.ToString();
                PriceTextBox.Text = selectedConversion.SellingPrice.ToString();
                UnitCostPriceTextBox.Text = selectedConversion.CostPrice?.ToString();
                UnitBarcodeTextBox.Text = selectedConversion.Barcode;
                IsStockOnlyCheckBox.IsChecked = selectedConversion.IsStockOnly;
            }
        }

        private void AddNewProductButton_Click(object sender, RoutedEventArgs e)
        {
            AddProductWindow addDialog = new AddProductWindow();
            addDialog.Owner = this;
            if (addDialog.ShowDialog() == true)
            {
                LoadAllProducts(true);
            }
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            FilterProducts(SearchTextBox.Text);
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            SearchTextBox.Clear();
            SearchTextBox.Focus();
        }

        private void DeleteProductButton_Click(object sender, RoutedEventArgs e)
        {
            if (ProductsListView.SelectedItem is not Product selectedProduct)
            {
                MessageBox.Show("Silakan pilih produk yang ingin dihapus dari daftar.", "Peringatan", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            MessageBoxResult result = MessageBox.Show($"Apakah Anda yakin ingin menghapus produk '{selectedProduct.ProductName}' secara permanen? Semua data terkait juga akan dihapus.", "Konfirmasi Hapus", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                using (var con = new SqlConnection(connectionString))
                {
                    con.Open();
                    SqlTransaction transaction = con.BeginTransaction();

                    try
                    {
                        string deleteConversionsQuery = "DELETE FROM ProductUnitConversions WHERE ProductID = @ProductID";
                        using (var cmd = new SqlCommand(deleteConversionsQuery, con, transaction))
                        {
                            cmd.Parameters.AddWithValue("@ProductID", selectedProduct.ProductId);
                            cmd.ExecuteNonQuery();
                        }

                        string deleteProductQuery = "DELETE FROM Products WHERE ProductID = @ProductID";
                        using (var cmd = new SqlCommand(deleteProductQuery, con, transaction))
                        {
                            cmd.Parameters.AddWithValue("@ProductID", selectedProduct.ProductId);
                            cmd.ExecuteNonQuery();
                        }

                        transaction.Commit();
                        MessageBox.Show("Produk berhasil dihapus.", "Sukses", MessageBoxButton.OK, MessageBoxImage.Information);
                        LoadAllProducts(true);
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        MessageBox.Show("Gagal menghapus produk. Error: " + ex.Message, "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void DuplicateProductButton_Click(object sender, RoutedEventArgs e)
        {
            if (ProductsListView.SelectedItem is not Product originalProduct)
            {
                MessageBox.Show("Silakan pilih produk yang ingin diduplikasi.", "Peringatan", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string newProductName = originalProduct.ProductName + " - COPY";
            int copyCount = 1;
            while (_allProducts.Any(p => p.ProductName.Equals(newProductName, StringComparison.OrdinalIgnoreCase)))
            {
                copyCount++;
                newProductName = $"{originalProduct.ProductName} - COPY {copyCount}";
            }

            using (var con = new SqlConnection(connectionString))
            {
                con.Open();
                SqlTransaction transaction = con.BeginTransaction();

                try
                {
                    string insertProductQuery = @"
                        INSERT INTO Products (ProductName, CostPrice, Stock, WarningStockLevel, WarningStockUnitID, AlternateNames, IsBundle)
                        OUTPUT INSERTED.ProductID
                        VALUES (@ProductName, @CostPrice, 0, @WarningStockLevel, @WarningStockUnitID, @AlternateNames, @IsBundle);";

                    int newProductId;
                    using (var cmd = new SqlCommand(insertProductQuery, con, transaction))
                    {
                        cmd.Parameters.AddWithValue("@ProductName", newProductName);
                        cmd.Parameters.AddWithValue("@CostPrice", originalProduct.CostPrice.HasValue ? (object)originalProduct.CostPrice.Value : DBNull.Value);
                        cmd.Parameters.AddWithValue("@WarningStockLevel", originalProduct.WarningStockLevel);
                        cmd.Parameters.AddWithValue("@WarningStockUnitID", originalProduct.WarningStockUnitID);
                        cmd.Parameters.AddWithValue("@AlternateNames", string.IsNullOrWhiteSpace(originalProduct.AlternateNames) ? DBNull.Value : originalProduct.AlternateNames);
                        cmd.Parameters.AddWithValue("@IsBundle", originalProduct.IsBundle);
                        newProductId = (int)cmd.ExecuteScalar();
                    }

                    string copyConversionsQuery = @"
                        INSERT INTO ProductUnitConversions (ProductID, UnitID, ConversionFactor, SellingPrice, CostPrice, Barcode, IsStockOnly)
                        SELECT @NewProductID, UnitID, ConversionFactor, SellingPrice, CostPrice, Barcode, IsStockOnly
                        FROM ProductUnitConversions
                        WHERE ProductID = @OriginalProductID;";

                    using (var cmd = new SqlCommand(copyConversionsQuery, con, transaction))
                    {
                        cmd.Parameters.AddWithValue("@NewProductID", newProductId);
                        cmd.Parameters.AddWithValue("@OriginalProductID", originalProduct.ProductId);
                        cmd.ExecuteNonQuery();
                    }

                    transaction.Commit();
                    MessageBox.Show($"Produk '{originalProduct.ProductName}' berhasil diduplikasi menjadi '{newProductName}'.", "Sukses", MessageBoxButton.OK, MessageBoxImage.Information);
                    LoadAllProducts(true);
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    MessageBox.Show("Gagal menduplikasi produk. Error: " + ex.Message, "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void PrintBarcodeButton_Click(object sender, RoutedEventArgs e)
        {
            var allPrintableItems = new List<BarcodePrintItem>();
            var allConversions = GetAllConversions();

            foreach (var product in _allProducts)
            {
                var productConversions = allConversions.Where(c => c.ProductID == product.ProductId);
                foreach (var conversion in productConversions)
                {
                    if (!string.IsNullOrWhiteSpace(conversion.Barcode))
                    {
                        allPrintableItems.Add(new BarcodePrintItem
                        {
                            ProductName = product.ProductName,
                            UnitName = conversion.UnitName,
                            Price = conversion.SellingPrice,
                            Barcode = conversion.Barcode,
                            Copies = 0
                        });
                    }
                }
            }

            if (allPrintableItems.Count == 0)
            {
                MessageBox.Show("Tidak ada satuan produk yang memiliki barcode untuk dicetak.", "Informasi", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            PrintQueueWindow printWindow = new PrintQueueWindow(allPrintableItems);
            printWindow.Owner = this;
            printWindow.ShowDialog();
        }

        private void PrintPriceLabelButton_Click(object sender, RoutedEventArgs e)
        {
            PriceLabelPrintWindow priceLabelWindow = new PriceLabelPrintWindow();
            priceLabelWindow.Owner = this;
            priceLabelWindow.Show();
        }

        private void DataAnalysisButton_Click(object sender, RoutedEventArgs e)
        {
            DataAnalysisWindow analysisWindow = new DataAnalysisWindow(this);
            analysisWindow.Owner = this;
            analysisWindow.Show();
        }

        private void SaveChangesButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedProduct == null) return;
            if (!int.TryParse(WarningStockLevelTextBox.Text, out int warningLevel))
            {
                MessageBox.Show("Ambang batas peringatan harus berupa angka.");
                return;
            }

            decimal? costPrice = null;
            if (!string.IsNullOrWhiteSpace(CostPriceTextBox.Text))
            {
                if (!decimal.TryParse(CostPriceTextBox.Text, out decimal parsedCostPrice))
                {
                    MessageBox.Show("Harga Awal harus berupa angka.");
                    return;
                }
                costPrice = parsedCostPrice;
            }

            string updatedProductName = ProductNameTextBox.Text;
            string updatedAlternateNames = AlternateNamesTextBox.Text;
            bool isBundle = _selectedProduct.IsBundle;

            using (SqlConnection checkCon = new SqlConnection(connectionString))
            {
                try
                {
                    checkCon.Open();
                    string checkQuery = "SELECT COUNT(1) FROM Products WHERE ProductName = @ProductName AND ProductID != @ProductID";
                    using (SqlCommand checkCmd = new SqlCommand(checkQuery, checkCon))
                    {
                        checkCmd.Parameters.AddWithValue("@ProductName", updatedProductName);
                        checkCmd.Parameters.AddWithValue("@ProductID", _selectedProduct.ProductId);
                        int existingCount = (int)checkCmd.ExecuteScalar();
                        if (existingCount > 0)
                        {
                            MessageBox.Show("Nama produk sudah digunakan oleh produk lain.", "Duplikat Ditemukan", MessageBoxButton.OK, MessageBoxImage.Error);
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

            string query = @"UPDATE Products SET 
                                 ProductName = @ProductName, 
                                 CostPrice = @CostPrice,
                                 WarningStockLevel = @WarningLevel, 
                                 WarningStockUnitID = @WarningUnitID,
                                 AlternateNames = @AlternateNames,
                                 IsBundle = @IsBundle
                               WHERE ProductID = @ProductID;";

            using (var con = new SqlConnection(connectionString))
            using (var cmd = new SqlCommand(query, con))
            {
                cmd.Parameters.AddWithValue("@ProductName", updatedProductName);
                cmd.Parameters.AddWithValue("@CostPrice", costPrice.HasValue ? (object)costPrice.Value : DBNull.Value);
                cmd.Parameters.AddWithValue("@WarningLevel", warningLevel);
                cmd.Parameters.AddWithValue("@WarningUnitID", WarningStockUnitComboBox.SelectedValue ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@AlternateNames", string.IsNullOrWhiteSpace(updatedAlternateNames) ? (object)DBNull.Value : updatedAlternateNames);
                cmd.Parameters.AddWithValue("@IsBundle", isBundle);
                cmd.Parameters.AddWithValue("@ProductID", _selectedProduct.ProductId);
                try
                {
                    con.Open();
                    cmd.ExecuteNonQuery();
                    MessageBox.Show("Perubahan berhasil disimpan!");
                    LoadAllProducts(true);
                }
                catch (Exception ex) { MessageBox.Show("Gagal menyimpan perubahan: " + ex.Message); }
            }
        }

        private void AddUnitButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedProduct == null || UnitsComboBox.SelectedValue == null)
            {
                MessageBox.Show("Pilih produk dan satuan terlebih dahulu.");
                return;
            }

            if (!decimal.TryParse(FactorTextBox.Text, out decimal factor))
            {
                MessageBox.Show("Isi per Satuan harus berupa angka.");
                return;
            }

            bool isStockOnly = IsStockOnlyCheckBox.IsChecked == true;
            decimal price = 0;

            if (!isStockOnly)
            {
                if (!decimal.TryParse(PriceTextBox.Text, out price) || price < 100)
                {
                    MessageBox.Show("Harga Jual harus diisi dengan angka minimal 100 untuk satuan yang dijual.");
                    return;
                }
            }

            decimal? unitCostPrice = null;
            if (!string.IsNullOrWhiteSpace(UnitCostPriceTextBox.Text))
            {
                if (!decimal.TryParse(UnitCostPriceTextBox.Text, out decimal parsedCostPrice))
                {
                    MessageBox.Show("Harga Modal Satuan harus berupa angka.");
                    return;
                }
                unitCostPrice = parsedCostPrice;
            }

            int unitId = (int)UnitsComboBox.SelectedValue;

            string query = @"
                IF EXISTS (SELECT 1 FROM ProductUnitConversions WHERE ProductID = @ProductID AND UnitID = @UnitID)
                    UPDATE ProductUnitConversions SET ConversionFactor = @Factor, SellingPrice = @Price, CostPrice = @CostPrice, Barcode = @Barcode, IsStockOnly = @IsStockOnly WHERE ProductID = @ProductID AND UnitID = @UnitID;
                ELSE
                    INSERT INTO ProductUnitConversions (ProductID, UnitID, ConversionFactor, SellingPrice, CostPrice, Barcode, IsStockOnly) VALUES (@ProductID, @UnitID, @Factor, @Price, @CostPrice, @Barcode, @IsStockOnly);";

            using (var con = new SqlConnection(connectionString))
            using (var cmd = new SqlCommand(query, con))
            {
                cmd.Parameters.AddWithValue("@ProductID", _selectedProduct.ProductId);
                cmd.Parameters.AddWithValue("@UnitID", unitId);
                cmd.Parameters.AddWithValue("@Factor", factor);
                cmd.Parameters.AddWithValue("@Price", isStockOnly ? (object)DBNull.Value : price);
                cmd.Parameters.AddWithValue("@CostPrice", unitCostPrice.HasValue ? (object)unitCostPrice.Value : DBNull.Value);
                cmd.Parameters.AddWithValue("@Barcode", string.IsNullOrWhiteSpace(UnitBarcodeTextBox.Text) ? DBNull.Value : UnitBarcodeTextBox.Text);
                cmd.Parameters.AddWithValue("@IsStockOnly", isStockOnly);

                try
                {
                    con.Open();
                    cmd.ExecuteNonQuery();
                    MessageBox.Show("Satuan berhasil disimpan!");
                    LoadConversionsForProduct(_selectedProduct.ProductId);
                    ClearUnitInputFields();
                }
                catch (Exception ex) { MessageBox.Show("Gagal menyimpan satuan: " + ex.Message); }
            }
        }

        private void RemoveUnitButton_Click(object sender, RoutedEventArgs e)
        {
            if (ConversionsDataGrid.SelectedItem is ProductUnitConversion selectedConversion)
            {
                if (selectedConversion.ConversionFactor == 1)
                {
                    MessageBox.Show("Satuan dasar (dengan isi 1) tidak boleh dihapus.");
                    return;
                }

                if (MessageBox.Show($"Yakin ingin menghapus satuan '{selectedConversion.UnitName}'?", "Konfirmasi Hapus", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                {
                    string query = "DELETE FROM ProductUnitConversions WHERE ConversionID = @ConversionID;";
                    using (var con = new SqlConnection(connectionString))
                    using (var cmd = new SqlCommand(query, con))
                    {
                        cmd.Parameters.AddWithValue("@ConversionID", selectedConversion.ConversionID);
                        try
                        {
                            con.Open();
                            cmd.ExecuteNonQuery();
                            MessageBox.Show("Satuan berhasil dihapus!");
                            LoadConversionsForProduct(_selectedProduct!.ProductId);
                            ClearUnitInputFields();
                        }
                        catch (Exception ex) { MessageBox.Show("Gagal menghapus satuan: " + ex.Message); }
                    }
                }
            }
            else
            {
                MessageBox.Show("Pilih satuan dari tabel untuk dihapus.");
            }
        }

        private void AddStockButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedProduct == null || AddStockUnitComboBox.SelectedItem == null)
            {
                MessageBox.Show("Pilih produk dan satuan untuk menambah stok.");
                return;
            }

            if (!int.TryParse(AddStockQuantityTextBox.Text, out int quantity))
            {
                MessageBox.Show("Jumlah stok harus berupa angka.");
                return;
            }

            var selectedConversion = AddStockUnitComboBox.SelectedItem as ProductUnitConversion;
            if (selectedConversion == null) return;

            decimal stockToAdd = quantity * selectedConversion.ConversionFactor;

            string query = "UPDATE Products SET Stock = Stock + @StockToAdd WHERE ProductID = @ProductID;";
            using (var con = new SqlConnection(connectionString))
            using (var cmd = new SqlCommand(query, con))
            {
                cmd.Parameters.AddWithValue("@StockToAdd", stockToAdd);
                cmd.Parameters.AddWithValue("@ProductID", _selectedProduct.ProductId);
                try
                {
                    con.Open();
                    cmd.ExecuteNonQuery();
                    MessageBox.Show("Stok berhasil ditambahkan!");
                    LoadAllProducts(true);
                }
                catch (Exception ex) { MessageBox.Show("Gagal menambah stok: " + ex.Message); }
            }
        }

        private void ManageUnitsButton_Click(object sender, RoutedEventArgs e)
        {
            ManageUnitsWindow manageUnits = new ManageUnitsWindow(connectionString);
            manageUnits.Owner = this;
            manageUnits.ShowDialog();
            LoadAllUnits();
        }

        private void ManageUsersButton_Click(object sender, RoutedEventArgs e)
        {
            UserManagerWindow userManager = new UserManagerWindow(connectionString);
            userManager.Owner = this;
            userManager.ShowDialog();
        }

        private void ViewReportsButton_Click(object sender, RoutedEventArgs e)
        {
            ReportsWindow reports = new ReportsWindow(connectionString, _allProducts);
            reports.Owner = this;
            reports.Show();
        }
        #endregion

        #region Helper Methods
        private void ToDoListButton_Click(object sender, RoutedEventArgs e)
        {
            ToDoListWindow toDoWindow = new ToDoListWindow(connectionString);
            toDoWindow.Owner = this;
            toDoWindow.Show();
        }

        private void MarkToDo_Click(object sender, RoutedEventArgs e)
        {
            if (ProductsListView.SelectedItem is not Product selectedProduct)
            {
                MessageBox.Show("Pilih produk terlebih dahulu dari daftar.", "Peringatan", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (sender is not MenuItem menuItem || menuItem.Tag == null) return;

            string taskType = menuItem.Tag.ToString()!;
            string prompt = $"Masukkan deskripsi untuk tugas '{taskType}' pada produk '{selectedProduct.ProductName}':";

            InputDialog inputDialog = new InputDialog(prompt, "Buat Tugas Baru");
            inputDialog.Owner = this;

            if (inputDialog.ShowDialog() == true)
            {
                string description = inputDialog.ResponseText;
                string query = "INSERT INTO ToDoItems (ProductID, TaskType, Description) VALUES (@ProductID, @TaskType, @Description);";

                using (var con = new SqlConnection(connectionString))
                using (var cmd = new SqlCommand(query, con))
                {
                    cmd.Parameters.AddWithValue("@ProductID", selectedProduct.ProductId);
                    cmd.Parameters.AddWithValue("@TaskType", taskType);
                    cmd.Parameters.AddWithValue("@Description", description);

                    try
                    {
                        con.Open();
                        cmd.ExecuteNonQuery();
                        MessageBox.Show("Tugas berhasil ditambahkan ke To-Do List.", "Sukses", MessageBoxButton.OK, MessageBoxImage.Information);
                        LoadAllProducts(true);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Gagal menyimpan tugas: " + ex.Message, "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }
        private void FilterProducts(string searchTerm)
        {
            ProductsList.Clear();
            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                foreach (var product in _allProducts)
                {
                    ProductsList.Add(product);
                }
                return;
            }

            searchTerm = searchTerm.ToLower();
            var allConversions = GetAllConversions();

            var productIdsFromUnitBarcode = allConversions
                .Where(c => c.Barcode != null && c.Barcode.ToLower().Contains(searchTerm))
                .Select(c => c.ProductID)
                .Distinct();

            var filteredList = _allProducts.Where(p =>
                p.ProductName.ToLower().Contains(searchTerm) ||
                (!string.IsNullOrEmpty(p.AlternateNames) && p.AlternateNames.ToLower().Contains(searchTerm)) ||
                productIdsFromUnitBarcode.Contains(p.ProductId)
            ).ToList();

            foreach (var product in filteredList)
            {
                ProductsList.Add(product);
            }
        }

        public List<Product> GetAllProductsForAnalysis() => _allProducts;
        public List<ProductUnitConversion> GetAllConversionsForAnalysis() => GetAllConversions();

        private List<ProductUnitConversion> GetAllConversions()
        {
            var allConversions = new List<ProductUnitConversion>();
            string query = @"SELECT c.ProductID, c.ConversionID, u.UnitName, c.ConversionFactor, c.SellingPrice, c.UnitID, c.CostPrice, c.Barcode, c.IsStockOnly 
                             FROM ProductUnitConversions c
                             JOIN Units u ON c.UnitID = u.UnitID;";
            using (var con = new SqlConnection(connectionString))
            using (var cmd = new SqlCommand(query, con))
            {
                try
                {
                    con.Open();
                    var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        allConversions.Add(new ProductUnitConversion
                        {
                            ProductID = reader.GetInt32(0),
                            ConversionID = reader.GetInt32(1),
                            UnitName = reader.GetString(2),
                            ConversionFactor = reader.GetDecimal(3),
                            SellingPrice = reader.IsDBNull(4) ? 0 : reader.GetDecimal(4),
                            UnitID = reader.GetInt32(5),
                            CostPrice = reader.IsDBNull(6) ? (decimal?)null : reader.GetDecimal(6),
                            Barcode = reader.IsDBNull(7) ? "" : reader.GetString(7),
                            IsStockOnly = reader.GetBoolean(8)
                        });
                    }
                }
                catch (Exception ex) { MessageBox.Show("Gagal memuat semua konversi: " + ex.Message); }
            }
            return allConversions;
        }

        private void ClearUnitInputFields()
        {
            UnitsComboBox.SelectedIndex = -1;
            FactorTextBox.Clear();
            PriceTextBox.Clear();
            UnitCostPriceTextBox.Clear();
            UnitBarcodeTextBox.Clear();
            IsStockOnlyCheckBox.IsChecked = false;
            ConversionsDataGrid.SelectedIndex = -1;
        }

        private void CheckForLowStockItems()
        {
            var lowStockItems = ProductsList.Where(p => p.IsLowOnStock).ToList();
            if (lowStockItems.Count > 0)
            {
                string message = "Peringatan! Produk berikut memiliki stok rendah:\n\n";
                foreach (var item in lowStockItems.Take(10))
                {
                    message += $"- {item.ProductName}\n";
                }
                if (lowStockItems.Count > 10) message += "...dan lainnya.";

                MessageBox.Show(message, "Notifikasi Stok Rendah", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void UpdateAllLowStockStatus()
        {
            var allConversions = new Dictionary<int, List<ProductUnitConversion>>();
            string query = "SELECT ProductID, UnitID, ConversionFactor FROM ProductUnitConversions;";
            using (var con = new SqlConnection(connectionString))
            using (var cmd = new SqlCommand(query, con))
            {
                con.Open();
                var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    int productId = reader.GetInt32(0);
                    if (!allConversions.ContainsKey(productId))
                    {
                        allConversions[productId] = new List<ProductUnitConversion>();
                    }
                    allConversions[productId].Add(new ProductUnitConversion
                    {
                        UnitID = reader.GetInt32(1),
                        ConversionFactor = reader.GetDecimal(2)
                    });
                }
            }

            foreach (var product in _allProducts)
            {
                if (allConversions.ContainsKey(product.ProductId))
                {
                    var warningUnit = allConversions[product.ProductId].FirstOrDefault(c => c.UnitID == product.WarningStockUnitID);
                    decimal conversionFactor = warningUnit?.ConversionFactor ?? 1;
                    decimal warningThresholdInBase = product.WarningStockLevel * conversionFactor;
                    product.IsLowOnStock = product.Stock < warningThresholdInBase;
                }
            }
        }

        private string CalculateStockBreakdown(int totalStockInBase, List<ProductUnitConversion> conversions)
        {
            if (conversions == null || conversions.Count == 0)
            {
                return $"{totalStockInBase} (Satuan Dasar)";
            }

            var sortedConversions = conversions.OrderByDescending(c => c.ConversionFactor).ToList();
            var breakdownParts = new List<string>();
            var remainder = totalStockInBase;

            foreach (var unit in sortedConversions)
            {
                if (unit.ConversionFactor <= 0) continue;

                int count = (int)(remainder / unit.ConversionFactor);
                if (count > 0)
                {
                    breakdownParts.Add($"{count} {unit.UnitName}");
                    remainder %= (int)unit.ConversionFactor;
                }
            }

            if (breakdownParts.Count == 0 && remainder == 0)
            {
                var baseUnit = sortedConversions.FirstOrDefault(c => c.ConversionFactor == 1);
                return $"0 {baseUnit?.UnitName ?? "Satuan Dasar"}";
            }

            return string.Join(", ", breakdownParts);
        }

        private void EditorPanel_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe)
            {
                var parent = VisualTreeHelper.GetParent(fe);
                while (parent != null && !(parent is GroupBox))
                {
                    parent = VisualTreeHelper.GetParent(parent);
                }

                if (parent is GroupBox groupBox)
                {
                    if (groupBox.Parent is Border border)
                    {
                        UpdateActiveSectionHighlight(border);
                    }
                }
            }
        }

        private void UpdateActiveSectionHighlight(Border activeBorder)
        {
            ProductDetailBorder.BorderBrush = Brushes.Transparent;
            UnitEditorBorder.BorderBrush = Brushes.Transparent;
            StockEditorBorder.BorderBrush = Brushes.Transparent;

            activeBorder.BorderBrush = Brushes.DodgerBlue;
        }

        private void ViewToDoButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedProduct == null) return;

            var tasks = new StringBuilder();
            string query = "SELECT Description FROM ToDoItems WHERE ProductID = @ProductID AND IsCompleted = 0 ORDER BY CreatedAt DESC;";

            using (var con = new SqlConnection(connectionString))
            using (var cmd = new SqlCommand(query, con))
            {
                cmd.Parameters.AddWithValue("@ProductID", _selectedProduct.ProductId);
                try
                {
                    con.Open();
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (!reader.HasRows)
                        {
                            MessageBox.Show($"Tidak ada tugas aktif untuk produk '{_selectedProduct.ProductName}'.", "Informasi Tugas", MessageBoxButton.OK, MessageBoxImage.Information);
                            return;
                        }
                        while (reader.Read())
                        {
                            tasks.AppendLine("- " + reader.GetString(0));
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Gagal mengambil daftar tugas: " + ex.Message, "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }

            MessageBox.Show($"Tugas untuk '{_selectedProduct.ProductName}':\n\n{tasks.ToString()}", "Daftar Tugas", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void IsStockOnlyCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            bool isChecked = IsStockOnlyCheckBox.IsChecked == true;
            PriceTextBox.IsEnabled = !isChecked;
            if (isChecked)
            {
                PriceTextBox.Text = "0";
            }
        }
        #endregion
    }

    #region Data Models

    public class Product : INotifyPropertyChanged
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public decimal? CostPrice { get; set; }
        public int WarningStockLevel { get; set; }
        public int WarningStockUnitID { get; set; }
        public string AlternateNames { get; set; } = string.Empty;
        public bool IsBundle { get; set; }
        public ObservableCollection<ProductLink> Components { get; set; } = new ObservableCollection<ProductLink>();
        public bool HasToDo { get; set; }

        private int _stock;
        public int Stock
        {
            get => _stock;
            set
            {
                _stock = value;
                OnPropertyChanged(nameof(Stock));
            }
        }

        public void RecalculateStock(List<Product> allProducts)
        {
            if (!IsBundle) return;

            if (!Components.Any())
            {
                Stock = 0;
                return;
            }

            int maxPossible = int.MaxValue;
            foreach (var component in Components)
            {
                var fullComponent = allProducts.FirstOrDefault(p => p.ProductId == component.ComponentProductId);
                if (fullComponent == null || component.QuantityPerBundle <= 0)
                {
                    maxPossible = 0;
                    break;
                }

                int possibleFromThis = (int)(fullComponent.Stock / component.QuantityPerBundle);
                if (possibleFromThis < maxPossible)
                {
                    maxPossible = possibleFromThis;
                }
            }
            Stock = maxPossible;
        }

        private bool _isLowOnStock;
        public bool IsLowOnStock
        {
            get => _isLowOnStock;
            set
            {
                _isLowOnStock = value;
                OnPropertyChanged(nameof(IsLowOnStock));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class Unit
    {
        public int UnitID { get; set; }
        public string UnitName { get; set; } = string.Empty;
        public int DefaultWarningThreshold { get; set; }
    }

    public class ProductUnitConversion
    {
        public int ProductID { get; set; }
        public int ConversionID { get; set; }
        public string UnitName { get; set; } = string.Empty;
        public decimal ConversionFactor { get; set; }
        public decimal SellingPrice { get; set; }
        public int UnitID { get; set; }
        public decimal? CostPrice { get; set; }
        public string Barcode { get; set; } = string.Empty;
        public bool IsStockOnly { get; set; }
    }

    public class ProductLink
    {
        public int LinkId { get; set; }
        public int ComponentProductId { get; set; }
        public string ComponentName { get; set; } = string.Empty;
        public decimal QuantityPerBundle { get; set; }
    }

    #endregion
}
