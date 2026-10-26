using Library_Project.Model;
using Library_Project.Services;
using Library_Project.Services.Interfaces;
using Moq;

namespace Library_Project_Tests
{
    public class LibraryServiceTests
    {
        private readonly Mock<IBookRepository> _repoMock;
        private readonly Mock<IMemberService> _memberMock;
        private readonly Mock<INotificationService> _notifMock;
        private readonly LibraryService _service;

        public LibraryServiceTests()
        {
            _repoMock = new Mock<IBookRepository>();
            _memberMock = new Mock<IMemberService>();
            _notifMock = new Mock<INotificationService>();

            _service = new LibraryService(_repoMock.Object, _memberMock.Object, _notifMock.Object);
        }

        #region AddBook

        /// <summary>
        /// [Theory] / [InlineData] + Assert.Throws: AddBook кидає ArgumentException,
        /// якщо назва книги порожня, null або складається лише з пробілів.
        /// </summary>
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void AddBook_InvalidTitle_ThrowsArgumentException(string? title)
        {
            var ex = Assert.Throws<ArgumentException>(() => _service.AddBook(title!, 5));

            Assert.Equal("Title required.", ex.Message);
        }

        /// <summary>
        /// [Theory] / [InlineData] + Verify(..., Times.Never): при некоректній кількості копій
        /// (нуль або від'ємне число) кидається виняток, а репозиторій не викликається взагалі.
        /// </summary>
        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(-100)]
        public void AddBook_NonPositiveCopies_ThrowsAndNeverTouchesRepository(int copies)
        {
            Assert.Throws<ArgumentException>(() => _service.AddBook("Clean Code", copies));

            _repoMock.Verify(r => r.FindBook(It.IsAny<string>()), Times.Never);
            _repoMock.Verify(r => r.SaveBook(It.IsAny<Book>()), Times.Never);
        }

        /// <summary>
        /// It.Is(predicate): якщо книги ще немає в репозиторії, AddBook зберігає
        /// новий об'єкт Book саме з переданими назвою та кількістю копій.
        /// </summary>
        [Fact]
        public void AddBook_NewBook_SavesBookWithGivenTitleAndCopies()
        {
            _repoMock.Setup(r => r.FindBook("Dune")).Returns((Book)null!);

            _service.AddBook("Dune", 3);

            _repoMock.Verify(r => r.SaveBook(It.Is<Book>(b => b.Title == "Dune" && b.Copies == 3)), Times.Once);
        }

        /// <summary>
        /// Assert.Equal + Verify(): якщо книга вже існує, AddBook додає копії до наявних
        /// і зберігає той самий об'єкт, а не створює новий.
        /// </summary>
        [Fact]
        public void AddBook_ExistingBook_IncreasesCopiesAndSavesSameInstance()
        {
            var existing = new Book { Title = "Dune", Copies = 2 };
            _repoMock.Setup(r => r.FindBook("Dune")).Returns(existing);

            _service.AddBook("Dune", 3);

            Assert.Equal(5, existing.Copies);
            _repoMock.Verify(r => r.SaveBook(existing));
        }

        /// <summary>
        /// Assert.Null / Assert.NotNull: за допомогою Callback перехоплюємо книгу, яку сервіс
        /// передає в SaveBook. До виклику AddBook її немає, після виклику вона створена.
        /// </summary>
        [Fact]
        public void AddBook_NewBook_CreatesBookObjectForRepository()
        {
            Book? savedBook = null;
            _repoMock.Setup(r => r.FindBook(It.IsAny<string>())).Returns((Book)null!);
            _repoMock.Setup(r => r.SaveBook(It.IsAny<Book>())).Callback<Book>(b => savedBook = b);

            Assert.Null(savedBook);

            _service.AddBook("1984", 1);

            Assert.NotNull(savedBook);
            Assert.Equal("1984", savedBook.Title);
        }

        #endregion

        #region BorrowBook

        /// <summary>
        /// Assert.True: BorrowBook повертає true, якщо користувач валідний
        /// і є хоча б одна доступна копія книги.
        /// </summary>
        [Fact]
        public void BorrowBook_ValidMemberAndAvailableBook_ReturnsTrue()
        {
            _memberMock.Setup(m => m.IsValidMember(1)).Returns(true);
            _repoMock.Setup(r => r.FindBook("Dune")).Returns(new Book { Title = "Dune", Copies = 2 });

            var result = _service.BorrowBook(1, "Dune");

            Assert.True(result);
        }

        /// <summary>
        /// Assert.NotEqual: після успішного позичання кількість копій книги
        /// відрізняється від початкової (зменшилась на одну).
        /// </summary>
        [Fact]
        public void BorrowBook_Success_ChangesCopiesCount()
        {
            const int initialCopies = 4;
            var book = new Book { Title = "Dune", Copies = initialCopies };
            _memberMock.Setup(m => m.IsValidMember(1)).Returns(true);
            _repoMock.Setup(r => r.FindBook("Dune")).Returns(book);

            _service.BorrowBook(1, "Dune");

            Assert.NotEqual(initialCopies, book.Copies);
            Assert.Equal(initialCopies - 1, book.Copies);
        }

        /// <summary>
        /// Assert.False + [Theory]: BorrowBook повертає false, якщо копій книги не залишилось
        /// (0 або некоректне від'ємне значення), і нічого не зберігає.
        /// </summary>
        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void BorrowBook_NoCopiesLeft_ReturnsFalse(int copies)
        {
            _memberMock.Setup(m => m.IsValidMember(It.IsAny<int>())).Returns(true);
            _repoMock.Setup(r => r.FindBook("Dune")).Returns(new Book { Title = "Dune", Copies = copies });

            var result = _service.BorrowBook(1, "Dune");

            Assert.False(result);
            _repoMock.Verify(r => r.SaveBook(It.IsAny<Book>()), Times.Never);
        }

        /// <summary>
        /// Assert.False + It.IsAny: якщо книги немає в репозиторії, BorrowBook повертає false
        /// і не надсилає жодного сповіщення.
        /// </summary>
        [Fact]
        public void BorrowBook_BookNotFound_ReturnsFalseAndDoesNotNotify()
        {
            _memberMock.Setup(m => m.IsValidMember(It.IsAny<int>())).Returns(true);
            _repoMock.Setup(r => r.FindBook(It.IsAny<string>())).Returns((Book)null!);

            var result = _service.BorrowBook(1, "Unknown");

            Assert.False(result);
            _notifMock.Verify(n => n.NotifyBorrow(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
        }

        /// <summary>
        /// Assert.Throws + Verify(..., Times.Never): для невалідного користувача BorrowBook
        /// кидає InvalidOperationException, книгу не шукає, не зберігає і не сповіщає.
        /// </summary>
        [Fact]
        public void BorrowBook_InvalidMember_ThrowsAndDoesNothingElse()
        {
            _memberMock.Setup(m => m.IsValidMember(42)).Returns(false);

            var ex = Assert.Throws<InvalidOperationException>(() => _service.BorrowBook(42, "Dune"));

            Assert.Equal("Invalid member.", ex.Message);
            _repoMock.Verify(r => r.FindBook(It.IsAny<string>()), Times.Never);
            _repoMock.Verify(r => r.SaveBook(It.IsAny<Book>()), Times.Never);
            _notifMock.Verify(n => n.NotifyBorrow(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
        }

        /// <summary>
        /// Verify() з конкретними аргументами: після успішного позичання сервіс зберігає книгу
        /// і надсилає сповіщення саме для цього користувача та цієї книги.
        /// </summary>
        [Fact]
        public void BorrowBook_Success_SavesBookAndNotifiesMember()
        {
            var book = new Book { Title = "Dune", Copies = 1 };
            _memberMock.Setup(m => m.IsValidMember(7)).Returns(true);
            _repoMock.Setup(r => r.FindBook("Dune")).Returns(book);

            _service.BorrowBook(7, "Dune");

            _repoMock.Verify(r => r.SaveBook(book));
            _notifMock.Verify(n => n.NotifyBorrow(7, "Dune"));
            _memberMock.Verify(m => m.IsValidMember(7), Times.Once);
        }

        /// <summary>
        /// Verify(..., Times.Exactly(n)): якщо позичити книгу тричі поспіль,
        /// збереження та сповіщення виконуються рівно три рази.
        /// </summary>
        [Fact]
        public void BorrowBook_CalledThreeTimes_NotifiesExactlyThreeTimes()
        {
            var book = new Book { Title = "Dune", Copies = 5 };
            _memberMock.Setup(m => m.IsValidMember(1)).Returns(true);
            _repoMock.Setup(r => r.FindBook("Dune")).Returns(book);

            _service.BorrowBook(1, "Dune");
            _service.BorrowBook(1, "Dune");
            _service.BorrowBook(1, "Dune");

            Assert.Equal(2, book.Copies);
            _repoMock.Verify(r => r.SaveBook(book), Times.Exactly(3));
            _notifMock.Verify(n => n.NotifyBorrow(1, "Dune"), Times.Exactly(3));
        }

        /// <summary>
        /// Verify(..., Times.AtMost(n)): якщо доступна лише одна копія, а позичити намагаються
        /// тричі, сповіщення про позичання надсилається не більше одного разу.
        /// </summary>
        [Fact]
        public void BorrowBook_OnlyOneCopy_NotifiesAtMostOnce()
        {
            var book = new Book { Title = "Dune", Copies = 1 };
            _memberMock.Setup(m => m.IsValidMember(It.IsAny<int>())).Returns(true);
            _repoMock.Setup(r => r.FindBook("Dune")).Returns(book);

            var results = new[]
            {
                _service.BorrowBook(1, "Dune"),
                _service.BorrowBook(2, "Dune"),
                _service.BorrowBook(3, "Dune")
            };

            Assert.Equal(new[] { true, false, false }, results);
            _notifMock.Verify(n => n.NotifyBorrow(It.IsAny<int>(), "Dune"), Times.AtMost(1));
        }

        #endregion

        #region ReturnBook

        /// <summary>
        /// Assert.True + Verify(..., Times.AtLeastOnce): повернення існуючої книги збільшує
        /// кількість копій і хоча б раз надсилає сповіщення про повернення.
        /// </summary>
        [Fact]
        public void ReturnBook_ExistingBook_IncreasesCopiesAndNotifies()
        {
            var book = new Book { Title = "Dune", Copies = 0 };
            _repoMock.Setup(r => r.FindBook("Dune")).Returns(book);

            var result = _service.ReturnBook(3, "Dune");

            Assert.True(result);
            Assert.Equal(1, book.Copies);
            _notifMock.Verify(n => n.NotifyReturn(3, "Dune"), Times.AtLeastOnce);
        }

        /// <summary>
        /// Assert.False + It.IsAny: повернення книги, якої немає в бібліотеці,
        /// повертає false і не викликає ні збереження, ні сповіщення.
        /// </summary>
        [Fact]
        public void ReturnBook_UnknownBook_ReturnsFalseAndDoesNotSave()
        {
            _repoMock.Setup(r => r.FindBook(It.IsAny<string>())).Returns((Book)null!);

            var result = _service.ReturnBook(3, "Unknown");

            Assert.False(result);
            _repoMock.Verify(r => r.SaveBook(It.IsAny<Book>()), Times.Never);
            _notifMock.Verify(n => n.NotifyReturn(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
        }

        #endregion

        #region GetAvailableBooks

        /// <summary>
        /// Assert.NotEmpty + Assert.Contains / DoesNotContain: GetAvailableBooks повертає лише
        /// книги, у яких є хоча б одна копія.
        /// </summary>
        [Fact]
        public void GetAvailableBooks_ReturnsOnlyBooksWithCopies()
        {
            var dune = new Book { Title = "Dune", Copies = 2 };
            var hobbit = new Book { Title = "The Hobbit", Copies = 1 };
            var outOfStock = new Book { Title = "1984", Copies = 0 };
            _repoMock.Setup(r => r.GetAllBooks()).Returns(new List<Book> { dune, outOfStock, hobbit });

            var result = _service.GetAvailableBooks();

            Assert.NotEmpty(result);
            Assert.Equal(2, result.Count);
            Assert.Contains(dune, result);
            Assert.Contains(result, b => b.Title == "The Hobbit");
            Assert.DoesNotContain(outOfStock, result);
        }

        /// <summary>
        /// Assert.NotNull + Assert.Empty: якщо всі книги розібрані, GetAvailableBooks
        /// повертає порожній список, а не null.
        /// </summary>
        [Fact]
        public void GetAvailableBooks_AllBooksBorrowed_ReturnsEmptyList()
        {
            _repoMock.Setup(r => r.GetAllBooks()).Returns(new List<Book>
            {
                new Book { Title = "Dune", Copies = 0 },
                new Book { Title = "1984", Copies = 0 }
            });

            var result = _service.GetAvailableBooks();

            Assert.NotNull(result);
            Assert.Empty(result);
            _repoMock.Verify(r => r.GetAllBooks(), Times.Once);
        }

        #endregion
    }
}
