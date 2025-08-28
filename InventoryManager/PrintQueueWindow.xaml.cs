using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZXing;
using ZXing.Common;
using ZXing.Windows.Compatibility;

namespace InventoryManager
{
    // Model untuk item dalam antrian cetak
    public class BarcodePrintItem : INotifyPropertyChanged
    {
        public string ProductName { get; set; }
        public string UnitName { get; set; }
        public decimal? Price { get; set; }
        public string Barcode { get; set; }

        private int _copies;
        public int Copies
        {
            get => _copies;
            set { _copies = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Copies))); }
        }

        public string DisplayName => $"{ProductName} ({UnitName})";

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public partial class PrintQueueWindow : Window
    {
        private List<BarcodePrintItem> _allPrintableItems;
        private ObservableCollection<BarcodePrintItem> _printQueue = new ObservableCollection<BarcodePrintItem>();
        private ObservableCollection<string> _paperSizes = new ObservableCollection<string>();
        private ObservableCollection<string> _barcodeSizes = new ObservableCollection<string>();

        private const double MmToDip = 96.0 / 25.4;

        // --- PAGINATION FIELDS ---
        private int _currentPage = 1;
        private int _totalPages = 1;
        private int _labelsPerPage = 1;

        public PrintQueueWindow(List<BarcodePrintItem> allPrintableItems)
        {
            InitializeComponent();
            _allPrintableItems = allPrintableItems;

            SourceItemsListView.ItemsSource = _allPrintableItems.GroupBy(i => i.ProductName).Select(g => g.First());
            PrintQueueDataGrid.ItemsSource = _printQueue;

            // Inisialisasi daftar ukuran
            InitializeSizes();
        }

        private void InitializeSizes()
        {
            // Ukuran Kertas Default
            _paperSizes.Add("A4 (210 x 297 mm)");
            PaperSizeComboBox.ItemsSource = _paperSizes;
            PaperSizeComboBox.SelectedIndex = 0;

            // Ukuran Barcode Default
            _barcodeSizes.Add("30 x 15");
            _barcodeSizes.Add("40 x 20");
            _barcodeSizes.Add("50 x 25");
            _barcodeSizes.Add("70 x 30");
            _barcodeSizes.Add("80 x 40");
            BarcodeSizeComboBox.ItemsSource = _barcodeSizes;
            BarcodeSizeComboBox.SelectedIndex = 2;
        }

        #region UI Event Handlers
        private void Settings_Changed(object sender, RoutedEventArgs e) => GeneratePreview();
        private void Copies_TextChanged(object sender, TextChangedEventArgs e) => GeneratePreview();

        private void SourceSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string searchTerm = SourceSearchTextBox.Text.ToLower();
            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                SourceItemsListView.ItemsSource = _allPrintableItems.GroupBy(i => i.ProductName).Select(g => g.First());
            }
            else
            {
                SourceItemsListView.ItemsSource = _allPrintableItems
                    .Where(i => i.ProductName.ToLower().Contains(searchTerm))
                    .GroupBy(i => i.ProductName)
                    .Select(g => g.First());
            }
        }

        private void SourceItemsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UnitsToPrintListView.ItemsSource = null;
            if (SourceItemsListView.SelectedItem is BarcodePrintItem selectedProduct)
            {
                UnitsToPrintListView.ItemsSource = _allPrintableItems.Where(i => i.ProductName == selectedProduct.ProductName);
            }
        }

        private void AddItemButton_Click(object sender, RoutedEventArgs e)
        {
            if (UnitsToPrintListView.SelectedItem is not BarcodePrintItem selectedItem)
            {
                MessageBox.Show("Pilih satuan produk terlebih dahulu.", "Peringatan", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Cek apakah item sudah ada di antrian
            var existingItem = _printQueue.FirstOrDefault(i => i.Barcode == selectedItem.Barcode && i.DisplayName == selectedItem.DisplayName);
            if (existingItem != null)
            {
                existingItem.Copies++; // Jika sudah ada, tambah jumlahnya
            }
            else
            {
                // Jika belum ada, tambahkan item baru ke antrian
                _printQueue.Add(new BarcodePrintItem
                {
                    ProductName = selectedItem.ProductName,
                    UnitName = selectedItem.UnitName,
                    Price = selectedItem.Price,
                    Barcode = selectedItem.Barcode,
                    Copies = 1
                });
            }
            GeneratePreview();
        }

        private void RemoveItemButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is BarcodePrintItem itemToRemove)
            {
                _printQueue.Remove(itemToRemove);
                GeneratePreview();
            }
        }

        private void PrintButton_Click(object sender, RoutedEventArgs e)
        {
            var paperSizeStr = GetCurrentPaperSizeStr();
            var barcodeSizeStr = GetCurrentBarcodeSizeStr();

            if (string.IsNullOrEmpty(paperSizeStr))
            {
                MessageBox.Show("Ukuran kertas belum dipilih.", "Input Tidak Valid", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrEmpty(barcodeSizeStr))
            {
                MessageBox.Show("Ukuran barcode belum dipilih.", "Input Tidak Valid", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            PrintDialog printDialog = new PrintDialog();
            if (printDialog.ShowDialog() == true)
            {
                var paginator = new BarcodePaginator(_printQueue.ToList(), paperSizeStr, barcodeSizeStr, GetCurrentMargin());
                printDialog.PrintDocument(paginator, "Cetak Barcode Produk");
            }
        }
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

        #region Size Management
        private void AddPaperSizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(PaperWidthTextBox.Text, out int w) && int.TryParse(PaperHeightTextBox.Text, out int h) && w > 0 && h > 0)
            {
                string newSize = $"Kustom ({w} x {h} mm)";
                if (!_paperSizes.Contains(newSize))
                {
                    _paperSizes.Add(newSize);
                }
                PaperSizeComboBox.SelectedItem = newSize;
                PaperWidthTextBox.Clear();
                PaperHeightTextBox.Clear();
            }
            else
            {
                MessageBox.Show("Masukkan lebar dan tinggi yang valid dalam angka (mm).", "Input Tidak Valid");
            }
        }

        private void DeletePaperSizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (PaperSizeComboBox.SelectedIndex > 0) // Jangan hapus A4
            {
                _paperSizes.RemoveAt(PaperSizeComboBox.SelectedIndex);
                PaperSizeComboBox.SelectedIndex = 0;
            }
        }

        private void AddBarcodeSizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(BarcodeWidthTextBox.Text, out int w) && int.TryParse(BarcodeHeightTextBox.Text, out int h) && w > 0 && h > 0)
            {
                string newSize = $"{w} x {h}";
                if (!_barcodeSizes.Contains(newSize))
                {
                    _barcodeSizes.Add(newSize);
                }
                BarcodeSizeComboBox.SelectedItem = newSize;
                BarcodeWidthTextBox.Clear();
                BarcodeHeightTextBox.Clear();
            }
            else
            {
                MessageBox.Show("Masukkan lebar dan tinggi yang valid dalam angka (mm).", "Input Tidak Valid");
            }
        }

        private void DeleteBarcodeSizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (BarcodeSizeComboBox.SelectedItem != null && _barcodeSizes.Count > 1)
            {
                _barcodeSizes.Remove(BarcodeSizeComboBox.SelectedItem.ToString()!);
                BarcodeSizeComboBox.SelectedIndex = 0;
            }
        }
        #endregion

        #region Preview Generation
        private void GeneratePreview()
        {
            if (!IsLoaded) return;

            PreviewCanvas.Children.Clear();

            if (!_printQueue.Any())
            {
                FitInfoTextBlock.Text = "Kalkulasi: -";
                return;
            }

            // Parse Ukuran Kertas
            if (PaperSizeComboBox.SelectedItem == null) return;
            string paperSizeStr = PaperSizeComboBox.SelectedItem.ToString()!;
            paperSizeStr = paperSizeStr.Substring(paperSizeStr.IndexOf('(') + 1).Replace("mm)", "").Trim();
            string[] paperParts = paperSizeStr.Split('x');
            double paperWidthDip = double.Parse(paperParts[0].Trim()) * MmToDip;
            double paperHeightDip = double.Parse(paperParts[1].Trim()) * MmToDip;

            PaperPreviewBorder.Width = paperWidthDip;
            PaperPreviewBorder.Height = paperHeightDip;
            PreviewCanvas.Width = paperWidthDip;
            PreviewCanvas.Height = paperHeightDip;

            // Parse Ukuran Barcode
            if (BarcodeSizeComboBox.SelectedItem == null) return;
            string[] barcodeParts = BarcodeSizeComboBox.SelectedItem.ToString()!.Split('x');
            double barcodeWidthMm = double.Parse(barcodeParts[0].Trim());
            double barcodeHeightMm = double.Parse(barcodeParts[1].Trim());
            double barcodeWidthDip = barcodeWidthMm * MmToDip;
            double barcodeHeightDip = barcodeHeightMm * MmToDip;

            double marginMm = double.TryParse(MarginTextBox.Text, out double m) ? m : 10.0;
            double marginDip = marginMm * MmToDip;

            // Kalkulasi
            int cols = (int)((paperWidthDip - (2 * marginDip)) / barcodeWidthDip);
            int rows = (int)((paperHeightDip - (2 * marginDip)) / barcodeHeightDip);
            _labelsPerPage = cols * rows;

            if (_labelsPerPage < 1)
            {
                FitInfoTextBlock.Text = "Ukuran barcode terlalu besar untuk kertas.";
                return;
            }

            var allBarcodesToPrint = _printQueue.SelectMany(item => Enumerable.Repeat(item, item.Copies)).ToList();
            _totalPages = (int)Math.Ceiling((double)allBarcodesToPrint.Count / _labelsPerPage);
            if (_totalPages == 0) _totalPages = 1;
            if (_currentPage > _totalPages) _currentPage = _totalPages;

            FitInfoTextBlock.Text = $"Muat: {_labelsPerPage} label per halaman ({cols} kolom x {rows} baris).";
            PageInfoTextBlock.Text = $"Halaman {_currentPage} dari {_totalPages}";
            PreviousPageButton.IsEnabled = _currentPage > 1;
            NextPageButton.IsEnabled = _currentPage < _totalPages;

            var itemsToPreview = allBarcodesToPrint.Skip((_currentPage - 1) * _labelsPerPage).Take(_labelsPerPage).ToList();

            // Gambar pratinjau
            int currentItemIndex = 0;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    if (currentItemIndex >= itemsToPreview.Count) break;

                    var item = itemsToPreview[currentItemIndex];
                    var barcodeVisual = CreateBarcodeVisual(item, barcodeWidthDip, barcodeHeightDip);

                    Canvas.SetLeft(barcodeVisual, marginDip + (c * barcodeWidthDip));
                    Canvas.SetTop(barcodeVisual, marginDip + (r * barcodeHeightDip));
                    PreviewCanvas.Children.Add(barcodeVisual);

                    currentItemIndex++;
                }
                if (currentItemIndex >= itemsToPreview.Count) break;
            }
        }

        private FrameworkElement CreateBarcodeVisual(BarcodePrintItem item, double width, double height)
        {
            var grid = new Grid { Width = width, Height = height };
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // For Barcode
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // For Name/Price

            // 1. Barcode Visual (Image)
            if (ShowBarcodeVisualCheckBox.IsChecked == true)
            {
                var writer = new BarcodeWriter
                {
                    Format = BarcodeFormat.CODE_128,
                    Options = new EncodingOptions
                    {
                        Height = (int)(height * 0.6),
                        Width = (int)width,
                        Margin = 2,
                        PureBarcode = (ShowBarcodeTextCheckBox.IsChecked == false)
                    }
                };
                var barcodeBitmap = writer.Write(item.Barcode);
                var barcodeImage = new Image
                {
                    Source = ToBitmapSource(barcodeBitmap),
                    Stretch = Stretch.Fill,
                    Margin = new Thickness(2)
                };
                Grid.SetRow(barcodeImage, 0);
                grid.Children.Add(barcodeImage);
            }

            // 2. Product Name and Price Text
            var textStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(2, 0, 2, 2) };
            if (ShowNameCheckBox.IsChecked == true)
            {
                textStack.Children.Add(new TextBlock { Text = item.DisplayName, FontSize = 8, TextTrimming = TextTrimming.CharacterEllipsis });
            }
            if (ShowPriceCheckBox.IsChecked == true)
            {
                textStack.Children.Add(new TextBlock { Text = item.Price?.ToString("C", new CultureInfo("id-ID")), FontSize = 9, FontWeight = FontWeights.Bold });
            }

            Grid.SetRow(textStack, 1);
            grid.Children.Add(textStack);

            return grid;
        }

        private BitmapSource ToBitmapSource(System.Drawing.Bitmap bitmap)
        {
            using (var memory = new System.IO.MemoryStream())
            {
                bitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Bmp);
                memory.Position = 0;
                var bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.StreamSource = memory;
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.EndInit();
                bitmapImage.Freeze();
                return bitmapImage;
            }
        }

        private string GetCurrentPaperSizeStr() => PaperSizeComboBox.SelectedItem as string;
        private string GetCurrentBarcodeSizeStr() => BarcodeSizeComboBox.SelectedItem as string;
        private double GetCurrentMargin() => double.TryParse(MarginTextBox.Text, out double m) ? m : 2.0;

        #endregion

        #region Paginator Class
        public class BarcodePaginator : DocumentPaginator
        {
            private readonly List<BarcodePrintItem> _items;
            private readonly Size _paperSize;
            private readonly Size _barcodeSize;
            private readonly double _margin;
            private readonly int _labelsPerPage;
            private readonly int _pageCount;

            public BarcodePaginator(List<BarcodePrintItem> items, string paperSizeStr, string barcodeSizeStr, double margin)
            {
                _items = items.SelectMany(item => Enumerable.Repeat(item, item.Copies)).ToList();

                paperSizeStr = paperSizeStr.Substring(paperSizeStr.IndexOf('(') + 1).Replace("mm)", "").Trim();
                var paperParts = paperSizeStr.Split('x');
                _paperSize = new Size(double.Parse(paperParts[0].Trim()) * MmToDip, double.Parse(paperParts[1].Trim()) * MmToDip);

                var barcodeParts = barcodeSizeStr.Split('x');
                _barcodeSize = new Size(double.Parse(barcodeParts[0].Trim()) * MmToDip, double.Parse(barcodeParts[1].Trim()) * MmToDip);

                _margin = margin * MmToDip;

                int cols = (int)((_paperSize.Width - (2 * _margin)) / _barcodeSize.Width);
                int rows = (int)((_paperSize.Height - (2 * _margin)) / _barcodeSize.Height);
                _labelsPerPage = cols * rows > 0 ? cols * rows : 1;
                _pageCount = (int)Math.Ceiling((double)_items.Count / _labelsPerPage);
            }

            public override DocumentPage GetPage(int pageNumber)
            {
                var pageVisual = new DrawingVisual();
                using (var dc = pageVisual.RenderOpen())
                {
                    var canvas = new Canvas();

                    int startIndex = pageNumber * _labelsPerPage;
                    var itemsOnPage = _items.Skip(startIndex).Take(_labelsPerPage).ToList();
                    int cols = (int)((_paperSize.Width - (2 * _margin)) / _barcodeSize.Width);
                    if (cols == 0) cols = 1;

                    for (int i = 0; i < itemsOnPage.Count; i++)
                    {
                        var item = itemsOnPage[i];
                        var barcodeVisual = CreateBarcodeVisual(item, _barcodeSize.Width, _barcodeSize.Height);

                        double x = _margin + (i % cols) * _barcodeSize.Width;
                        double y = _margin + (i / cols) * _barcodeSize.Height;

                        Canvas.SetLeft(barcodeVisual, x);
                        Canvas.SetTop(barcodeVisual, y);
                        canvas.Children.Add(barcodeVisual);
                    }

                    canvas.Measure(_paperSize);
                    canvas.Arrange(new Rect(_paperSize));

                    dc.DrawRectangle(new VisualBrush(canvas), null, new Rect(_paperSize));
                }
                return new DocumentPage(pageVisual, _paperSize, new Rect(_paperSize), new Rect(_paperSize));
            }

            public override bool IsPageCountValid => true;
            public override int PageCount => _pageCount;
            public override Size PageSize { get => _paperSize; set => throw new NotImplementedException(); }
            public override IDocumentPaginatorSource Source => null;

            private FrameworkElement CreateBarcodeVisual(BarcodePrintItem item, double width, double height)
            {
                var grid = new Grid { Width = width, Height = height };
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var writer = new BarcodeWriter
                {
                    Format = BarcodeFormat.CODE_128,
                    Options = new EncodingOptions { Height = (int)(height * 0.6), Width = (int)width, Margin = 2, PureBarcode = false }
                };
                var barcodeBitmap = writer.Write(item.Barcode);
                var barcodeImage = new Image { Source = ToBitmapSource(barcodeBitmap), Stretch = Stretch.Fill, Margin = new Thickness(2) };
                Grid.SetRow(barcodeImage, 0);
                grid.Children.Add(barcodeImage);

                var textStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(2, 0, 2, 2) };
                textStack.Children.Add(new TextBlock { Text = item.DisplayName, FontSize = 8, TextTrimming = TextTrimming.CharacterEllipsis });
                textStack.Children.Add(new TextBlock { Text = item.Price?.ToString("C", new CultureInfo("id-ID")), FontSize = 9, FontWeight = FontWeights.Bold });
                Grid.SetRow(textStack, 1);
                grid.Children.Add(textStack);

                grid.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                grid.Arrange(new Rect(new Size(width, height)));

                return grid;
            }

            private BitmapSource ToBitmapSource(System.Drawing.Bitmap bitmap)
            {
                using (var memory = new System.IO.MemoryStream())
                {
                    bitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Bmp);
                    memory.Position = 0;
                    var bitmapImage = new BitmapImage();
                    bitmapImage.BeginInit();
                    bitmapImage.StreamSource = memory;
                    bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                    bitmapImage.EndInit();
                    bitmapImage.Freeze();
                    return bitmapImage;
                }
            }
        }
        #endregion
    }
}
