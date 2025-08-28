using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Printing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZXing;
using ZXing.Common;
using ZXing.Windows.Compatibility;
using DrawingBrushes = System.Drawing.Brushes;
using WinFormsDialogResult = System.Windows.Forms.DialogResult;
using WinFormsPrintDialog = System.Windows.Forms.PrintDialog;
// Namespace untuk kelas Product dari MainWindow
// Pastikan kelas Product di MainWindow.xaml.cs memiliki properti SKU
// using static InventoryManager.MainWindow; 

// Alias untuk menghindari konflik nama
using WpfBrushes = System.Windows.Media.Brushes;
using WpfPrintDialog = System.Windows.Controls.PrintDialog;


namespace InventoryManager
{
    public partial class PriceLabelPrintWindow : Window, INotifyPropertyChanged
    {
        // --- DATA COLLECTIONS ---
        private List<PrintQueueItem> _allPrintableItems = new List<PrintQueueItem>();
        public ObservableCollection<PrintQueueItem> ProductsList { get; set; }
        public ObservableCollection<UnitToPrint> UnitsForSelectedProduct { get; set; }
        public ObservableCollection<PrintQueueItem> PrintQueue { get; set; }
        public ObservableCollection<PaperSize> PaperSizes { get; set; }
        public ObservableCollection<string> LabelSizes { get; set; }

        private readonly string _settingsFilePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "print_settings.conf");
        private const double MmToDip = 96.0 / 25.4;

        private int _printQueueTotalItems;
        public string PrintQueueTitle => $"Daftar Cetak ({_printQueueTotalItems} item)";

        // --- PAGINATION & PRINTING FIELDS ---
        private int _currentPage = 1;
        private int _totalPages = 1;
        private int _labelsPerPage = 1;
        private List<PrintQueueItem> _flattenedPrintQueue = new List<PrintQueueItem>();
        private int _currentPrintItemIndex = 0;

        public event PropertyChangedEventHandler? PropertyChanged;

        public PriceLabelPrintWindow()
        {
            InitializeComponent();
            DataContext = this;

            // --- INITIALIZE COLLECTIONS ---
            ProductsList = new ObservableCollection<PrintQueueItem>();
            UnitsForSelectedProduct = new ObservableCollection<UnitToPrint>();
            PrintQueue = new ObservableCollection<PrintQueueItem>();
            PaperSizes = new ObservableCollection<PaperSize>();
            LabelSizes = new ObservableCollection<string>();

            // --- SET DATASOURCES ---
            ProductsListView.ItemsSource = ProductsList;
            UnitsToPrintListView.ItemsSource = UnitsForSelectedProduct;
            PrintQueueDataGrid.ItemsSource = PrintQueue;
            PaperSizeComboBox.ItemsSource = PaperSizes;
            LabelSizeComboBox.ItemsSource = LabelSizes;

            // --- EVENT HANDLERS ---
            PrintQueue.CollectionChanged += (s, e) => { UpdateTotalItems(); GeneratePreview(); };
            Loaded += PriceLabelPrintWindow_Loaded;
            Closing += PriceLabelPrintWindow_Closing;
        }

        private void PriceLabelPrintWindow_Loaded(object sender, RoutedEventArgs e)
        {
            InitializeSizes();
            LoadSettings();
            LoadDataFromMainWindow();
            FilterProducts("");
            GeneratePreview();
        }

        private void PriceLabelPrintWindow_Closing(object? sender, CancelEventArgs e)
        {
            SaveSettings();
        }

        private void InitializeSizes()
        {
            PaperSizes.Add(new PaperSize("A4", 210, 297));
            PaperSizes.Add(new PaperSize("Letter", 216, 279));
            PaperSizes.Add(new PaperSize("Blueprint 80mm", 80, 165));
            PaperSizes.Add(new PaperSize("Blueprint 80x40mm", 80, 180));
            PaperSizeComboBox.SelectedIndex = 0;

            LabelSizes.Add("30x15");
            LabelSizes.Add("50x25");
            LabelSizes.Add("80x40");
            LabelSizes.Add("75x26");
            LabelSizes.Add("75x28");
            LabelSizeComboBox.SelectedIndex = 1;
        }

        private void LoadDataFromMainWindow()
        {
            var mainWindow = Application.Current.MainWindow as MainWindow;
            if (mainWindow == null) return;

            var allProducts = mainWindow.GetAllProductsForAnalysis();
            var allConversions = mainWindow.GetAllConversionsForAnalysis();

            _allPrintableItems = allConversions
                .Where(c => c.SellingPrice > 0)
                .Select(c => {
                    var product = allProducts.FirstOrDefault(p => p.ProductId == c.ProductID);
                    return new PrintQueueItem
                    {
                        ProductName = product?.ProductName ?? "N/A",
                        SKU = product?.SKU ?? "", // Mengambil SKU
                        UnitName = c.UnitName,
                        Price = c.SellingPrice
                    };
                }).ToList();
        }

        #region UI Event Handlers

        private void SearchProductTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            FilterProducts(SearchProductTextBox.Text);
        }

        private void RefreshProductsButton_Click(object sender, RoutedEventArgs e)
        {
            LoadDataFromMainWindow();
            FilterProducts(SearchProductTextBox.Text);
        }

        private void ProductsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UnitsForSelectedProduct.Clear();
            if (ProductsListView.SelectedItem is PrintQueueItem selectedProduct)
            {
                var units = _allPrintableItems
                    .Where(item => item.ProductName == selectedProduct.ProductName)
                    .Select(item => new UnitToPrint
                    {
                        DisplayName = $"{item.UnitName} ({item.Price:C0})",
                        UnitName = item.UnitName,
                        SKU = item.SKU, // SKU ditambahkan
                        Price = item.Price,
                        IsSelected = true
                    });

                foreach (var unit in units)
                {
                    UnitsForSelectedProduct.Add(unit);
                }
            }
        }

        private void AddItemButton_Click(object sender, RoutedEventArgs e)
        {
            if (ProductsListView.SelectedItem is not PrintQueueItem selectedProduct) return;

            var selectedUnits = UnitsForSelectedProduct.Where(u => u.IsSelected).ToList();
            if (!selectedUnits.Any())
            {
                MessageBox.Show("Pilih minimal satu satuan untuk ditambahkan.", "Peringatan", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string sku = selectedUnits.FirstOrDefault()?.SKU ?? selectedProduct.SKU;

            var newItem = new PrintQueueItem
            {
                ProductName = selectedProduct.ProductName,
                SKU = sku,
                Copies = 1,
                Units = selectedUnits.Select(u => new UnitPricingInfo { UnitName = u.UnitName, Price = u.Price }).ToList()
            };

            PrintQueue.Add(newItem);

            foreach (var unit in UnitsForSelectedProduct)
            {
                unit.IsSelected = false;
            }
            UpdateTotalItems();
        }

        private void RemoveItemButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is PrintQueueItem item)
            {
                PrintQueue.Remove(item);
            }
        }

        private void Copies_TextChanged(object sender, TextChangedEventArgs e) => GeneratePreview();
        private void Settings_Changed(object sender, RoutedEventArgs e) => GeneratePreview();

        #endregion

        #region Pagination Controls
        private void PreviousPageButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage > 1)
            {
                _currentPage--;
                GeneratePreview();
            }
        }

        private void NextPageButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage < _totalPages)
            {
                _currentPage++;
                GeneratePreview();
            }
        }
        #endregion

        #region Size Management & Settings

        private void AddPaperSizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (double.TryParse(PaperWidthTextBox.Text, out double w) && double.TryParse(PaperHeightTextBox.Text, out double h) && w > 0 && h > 0)
            {
                var newSize = new PaperSize($"Kustom {w}x{h}", w, h);
                if (!PaperSizes.Any(p => p.Name == newSize.Name)) PaperSizes.Add(newSize);
                PaperSizeComboBox.SelectedItem = PaperSizes.FirstOrDefault(p => p.Name == newSize.Name);
            }
        }

        private void AddLabelSizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (double.TryParse(LabelWidthTextBox.Text, out double w) && double.TryParse(LabelHeightTextBox.Text, out double h) && w > 0 && h > 0)
            {
                string newSize = $"{w}x{h}";
                if (!LabelSizes.Contains(newSize)) LabelSizes.Add(newSize);
                LabelSizeComboBox.SelectedItem = newSize;
            }
        }

        private void SaveSettings()
        {
            try
            {
                var settings = new Dictionary<string, string>
                {
                    ["LabelSize"] = LabelSizeComboBox.SelectedItem?.ToString() ?? "50x25",
                    ["Margin"] = MarginTextBox.Text,
                    ["ProductNameFont"] = ProductNameFontSizeTextBox.Text,
                    ["PriceFont"] = PriceFontSizeTextBox.Text
                };
                File.WriteAllLines(_settingsFilePath, settings.Select(kvp => $"{kvp.Key}={kvp.Value}"));
            }
            catch (Exception ex) { Console.WriteLine("Gagal menyimpan pengaturan: " + ex.Message); }
        }

        private void LoadSettings()
        {
            try
            {
                if (!File.Exists(_settingsFilePath)) return;
                var settings = File.ReadAllLines(_settingsFilePath).ToDictionary(l => l.Split('=')[0], l => l.Split('=')[1]);

                if (settings.TryGetValue("LabelSize", out var labelSize)) LabelSizeComboBox.SelectedItem = labelSize;
                if (settings.TryGetValue("Margin", out var margin)) MarginTextBox.Text = margin;
                if (settings.TryGetValue("ProductNameFont", out var pNameFont)) ProductNameFontSizeTextBox.Text = pNameFont;
                if (settings.TryGetValue("PriceFont", out var priceFont)) PriceFontSizeTextBox.Text = priceFont;
            }
            catch (Exception ex) { Console.WriteLine("Gagal memuat pengaturan: " + ex.Message); }
        }

        #endregion

        #region Helper & Preview Methods

        private void FilterProducts(string searchTerm)
        {
            ProductsList.Clear();
            var filtered = string.IsNullOrWhiteSpace(searchTerm)
                ? _allPrintableItems
                : _allPrintableItems.Where(p => p.ProductName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase));

            foreach (var p in filtered.GroupBy(p => p.ProductName).Select(g => g.First()))
            {
                ProductsList.Add(p);
            }
        }

        private void UpdateTotalItems()
        {
            _printQueueTotalItems = PrintQueue.Sum(item => item.Copies);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PrintQueueTitle)));
        }

        private void GeneratePreview()
        {
            if (!IsLoaded) return;
            PreviewCanvas.Children.Clear();

            var paper = GetCurrentPaperSize();
            var labelSizeStr = GetCurrentLabelSize();
            double margin_mm = GetCurrentMargin();

            if (paper == null || labelSizeStr == null) return;
            if (!TryParseMm(labelSizeStr, out double labelW_mm, out double labelH_mm)) return;

            double scale = Math.Min(500 / paper.Width, 600 / paper.Height);
            PaperPreviewBorder.Width = paper.Width * scale;
            PaperPreviewBorder.Height = paper.Height * scale;
            PreviewCanvas.Width = paper.Width * scale;
            PreviewCanvas.Height = paper.Height * scale;

            double printableWidth = paper.Width - (2 * margin_mm);
            double printableHeight = paper.Height - (2 * margin_mm);

            int cols = (int)(printableWidth / labelW_mm);
            int rows = (int)(printableHeight / labelH_mm);

            _labelsPerPage = cols * rows;

            if (_labelsPerPage < 1)
            {
                FitInfoTextBlock.Text = "Ukuran label terlalu besar untuk kertas.";
                return;
            }

            _flattenedPrintQueue = PrintQueue.SelectMany(item => Enumerable.Repeat(item, item.Copies)).ToList();
            _totalPages = (int)Math.Ceiling((double)_flattenedPrintQueue.Count / _labelsPerPage);
            if (_totalPages == 0) _totalPages = 1;
            if (_currentPage > _totalPages) _currentPage = _totalPages;

            FitInfoTextBlock.Text = $"Muat: {_labelsPerPage} label per halaman ({cols} kolom x {rows} baris).";
            PageInfoTextBlock.Text = $"Halaman {_currentPage} dari {_totalPages}";
            PreviousPageButton.IsEnabled = _currentPage > 1;
            NextPageButton.IsEnabled = _currentPage < _totalPages;

            var itemsToPreview = _flattenedPrintQueue.Skip((_currentPage - 1) * _labelsPerPage).Take(_labelsPerPage).ToList();

            if (!itemsToPreview.Any() && _flattenedPrintQueue.Any())
            {
                _currentPage = 1;
                itemsToPreview = _flattenedPrintQueue.Skip(0).Take(_labelsPerPage).ToList();
            }

            if (!itemsToPreview.Any())
            {
                itemsToPreview.Add(new PrintQueueItem
                {
                    ProductName = "Nama Produk",
                    SKU = "123456789",
                    Units = new List<UnitPricingInfo> { new UnitPricingInfo { UnitName = "Pcs", Price = 10000 } }
                });
            }

            for (int i = 0; i < itemsToPreview.Count; i++)
            {
                var item = itemsToPreview[i];
                var labelVisual = CreateLabelVisual(item);

                var viewBox = new Viewbox
                {
                    Width = labelW_mm * scale,
                    Height = labelH_mm * scale,
                    Child = labelVisual,
                    Stretch = System.Windows.Media.Stretch.Fill
                };

                Canvas.SetLeft(viewBox, (margin_mm + (i % cols) * labelW_mm) * scale);
                Canvas.SetTop(viewBox, (margin_mm + (i / cols) * labelH_mm) * scale);
                PreviewCanvas.Children.Add(viewBox);
            }
        }

        private FrameworkElement CreateLabelVisual(PrintQueueItem item)
        {
            var border = new Border
            {
                Padding = new Thickness(4),
                Background = WpfBrushes.White
            };

            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Nama
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Harga
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Barcode

            double.TryParse(ProductNameFontSizeTextBox.Text, out double pNameFont);
            double.TryParse(PriceFontSizeTextBox.Text, out double priceFont);

            var productNameBlock = new TextBlock
            {
                Text = item.ProductName,
                FontWeight = FontWeights.Bold,
                FontSize = pNameFont > 0 ? pNameFont : 12,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 2)
            };
            Grid.SetRow(productNameBlock, 0);
            mainGrid.Children.Add(productNameBlock);

            var pricePanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(pricePanel, 1);
            mainGrid.Children.Add(pricePanel);

            foreach (var unit in item.Units)
            {
                var priceBlock = new TextBlock
                {
                    Text = $"{unit.Price:C0} / {unit.UnitName}",
                    FontSize = priceFont > 0 ? priceFont : 14,
                    FontWeight = FontWeights.Normal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center
                };
                pricePanel.Children.Add(priceBlock);
            }

            var barcodeSource = GenerateBarcodeImageSource(item.SKU);
            if (barcodeSource != null)
            {
                var barcodeImage = new System.Windows.Controls.Image
                {
                    Source = barcodeSource,
                    Stretch = System.Windows.Media.Stretch.Uniform,
                    MinHeight = 20,
                    Margin = new Thickness(0, 4, 0, 0)
                };
                Grid.SetRow(barcodeImage, 2);
                mainGrid.Children.Add(barcodeImage);
            }

            var centeringGrid = new Grid { Children = { mainGrid } };
            mainGrid.VerticalAlignment = VerticalAlignment.Center;

            border.Child = centeringGrid;
            return border;
        }

        private bool TryParseMm(string text, out double width, out double height)
        {
            width = 0; height = 0;
            var parts = text.ToLower().Split('x');
            if (parts.Length != 2) return false;
            return double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out width) &&
                   double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out height);
        }

        private PaperSize GetCurrentPaperSize() => PaperSizeComboBox.SelectedItem as PaperSize;
        private string GetCurrentLabelSize() => LabelSizeComboBox.SelectedItem as string;
        private double GetCurrentMargin() => double.TryParse(MarginTextBox.Text, out double m) ? m : 2.0;

        #endregion

        #region Printing Logic (System.Drawing)

        private void PrintButton_Click(object sender, RoutedEventArgs e)
        {
            _flattenedPrintQueue = PrintQueue.SelectMany(item => Enumerable.Repeat(item, item.Copies)).ToList();

            if (_flattenedPrintQueue.Count == 0)
            {
                MessageBox.Show("Antrian cetak kosong.", "Informasi", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _currentPrintItemIndex = 0;

            using (var printDialog = new WinFormsPrintDialog())
            {
                PrintDocument pd = new PrintDocument();
                pd.PrintPage += pd_PrintPage;

                printDialog.Document = pd;

                if (printDialog.ShowDialog() == WinFormsDialogResult.OK)
                {
                    try
                    {
                        pd.Print();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Gagal mencetak: " + ex.Message, "Error Cetak", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void pd_PrintPage(object sender, PrintPageEventArgs e)
        {
            Graphics g = e.Graphics;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;

            var labelSizeStr = GetCurrentLabelSize();
            if (!TryParseMm(labelSizeStr, out double labelW_mm, out double labelH_mm)) return;

            float labelWidth = (float)(labelW_mm / 25.4 * 100);
            float labelHeight = (float)(labelH_mm / 25.4 * 100);

            RectangleF printableArea = e.MarginBounds;

            int cols = (int)(printableArea.Width / labelWidth);
            int rows = (int)(printableArea.Height / labelHeight);

            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < cols; col++)
                {
                    if (_currentPrintItemIndex >= _flattenedPrintQueue.Count) break;

                    var item = _flattenedPrintQueue[_currentPrintItemIndex];

                    float x = printableArea.Left + (col * labelWidth);
                    float y = printableArea.Top + (row * labelHeight);

                    using (Font nameFont = new Font("Arial", 8, System.Drawing.FontStyle.Bold))
                    using (Font priceFont = new Font("Arial", 9, System.Drawing.FontStyle.Regular))
                    {
                        g.DrawString(item.ProductName, nameFont, DrawingBrushes.Black, new RectangleF(x + 2, y + 2, labelWidth - 4, labelHeight * 0.4f));

                        string priceText = string.Join("\n", item.Units.Select(u => $"{u.Price:C0} / {u.UnitName}"));
                        g.DrawString(priceText, priceFont, DrawingBrushes.Black, new RectangleF(x + 2, y + 20, labelWidth - 4, labelHeight * 0.5f));

                        using (Bitmap barcodeBitmap = GenerateBarcode(item.SKU))
                        {
                            if (barcodeBitmap != null)
                            {
                                g.DrawImage(barcodeBitmap, new RectangleF(x + 5, y + 50, labelWidth - 10, labelHeight * 0.3f));
                            }
                        }
                    }

                    _currentPrintItemIndex++;
                }
                if (_currentPrintItemIndex >= _flattenedPrintQueue.Count) break;
            }

            e.HasMorePages = _currentPrintItemIndex < _flattenedPrintQueue.Count;
        }

        private Bitmap GenerateBarcode(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            try
            {
                var barcodeWriter = new BarcodeWriter
                {
                    Format = BarcodeFormat.CODE_128,
                    Options = new EncodingOptions { Height = 30, Width = 100, Margin = 0, PureBarcode = true }
                };
                return barcodeWriter.Write(text);
            }
            catch { return null; }
        }

        private ImageSource GenerateBarcodeImageSource(string text)
        {
            using (Bitmap bitmap = GenerateBarcode(text))
            {
                if (bitmap == null) return null;
                using (var stream = new MemoryStream())
                {
                    bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                    stream.Position = 0;
                    var bitmapImage = new BitmapImage();
                    bitmapImage.BeginInit();
                    bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                    bitmapImage.StreamSource = stream;
                    bitmapImage.EndInit();
                    bitmapImage.Freeze();
                    return bitmapImage;
                }
            }
        }

        #endregion

        #region Data Classes

        public class PrintQueueItem : INotifyPropertyChanged
        {
            public string ProductName { get; set; } = "";
            public string SKU { get; set; } = "";
            public string UnitName { get; set; } = "";
            public decimal Price { get; set; }
            public List<UnitPricingInfo> Units { get; set; } = new List<UnitPricingInfo>();

            private int _copies = 1;
            public int Copies
            {
                get => _copies;
                set { _copies = value; OnPropertyChanged(nameof(Copies)); }
            }

            public string DisplayName => $"{ProductName} ({(Units.Any() ? string.Join(", ", Units.Select(u => u.UnitName)) : UnitName)})";
            public event PropertyChangedEventHandler? PropertyChanged;
            protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public class UnitPricingInfo
        {
            public string UnitName { get; set; } = "";
            public decimal Price { get; set; }
        }

        public class UnitToPrint
        {
            public string DisplayName { get; set; } = "";
            public string UnitName { get; set; } = "";
            public string SKU { get; set; } = "";
            public decimal Price { get; set; }
            public bool IsSelected { get; set; }
        }

        public class PaperSize
        {
            public string Name { get; set; }
            public double Width { get; set; }
            public double Height { get; set; }
            public PaperSize(string name, double width, double height) { Name = name; Width = width; Height = height; }
            public override string ToString() => $"{Name} ({Width}x{Height} mm)";
        }

        #endregion
    }
}
