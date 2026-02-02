function initNavigation() {
    const header = document.getElementById('header');
    const mobileMenuBtn = document.getElementById('mobileMenuBtn');
    const mobileMenu = document.getElementById('mobileMenu');
    const navLinks = document.querySelectorAll('.nav-link, .mobile-nav-link');
    
    // Toggle mobile menu
    mobileMenuBtn.addEventListener('click', () => {
        mobileMenuBtn.classList.toggle('active');
        mobileMenu.classList.toggle('active');
    });
    
    // Close mobile menu when clicking a link
    navLinks.forEach(link => {
        link.addEventListener('click', (e) => {
            mobileMenuBtn.classList.remove('active');
            mobileMenu.classList.remove('active');
            updateActiveNavLink(link.dataset.section);
        });
    });
    
    // Handle scroll events for header styling and active section detection
    window.addEventListener('scroll', () => {
        if (window.scrollY > 20) {
            header.classList.add('scrolled');
        } else {
            header.classList.remove('scrolled');
        }
        updateActiveSectionOnScroll();
    });
}

function updateActiveNavLink(sectionId) {
    const allLinks = document.querySelectorAll('.nav-link, .mobile-nav-link');
    
    allLinks.forEach(link => {
        link.classList.remove('active');
        if (link.dataset.section === sectionId) 
        {
            link.classList.add('active');
        }
    });
}

function updateActiveSectionOnScroll() {
    const sections = ['home', 'about', 'game', 'contact'];
    const scrollPosition = window.scrollY + 100; 
    
    for (const sectionId of sections) {
        const section = document.getElementById(sectionId);
        if (section) 
        {
            const sectionTop = section.offsetTop;
            const sectionHeight = section.offsetHeight;
            if (scrollPosition >= sectionTop && scrollPosition < sectionTop + sectionHeight)
            {
                updateActiveNavLink(sectionId);
                break;
            }
        }
    }
}

class TicTacToeGame {
    constructor() {
        // Game state
        this.board = Array(9).fill(null);
        this.currentPlayer = 'X';
        this.winner = null;
        this.winningLine = [];
        this.gameActive = true;
        
        // DOM elements
        this.cells = document.querySelectorAll('.cell');
        this.statusDisplay = document.getElementById('gameStatus');
        this.resetBtn = document.getElementById('resetBtn');
        
        // Winning combinations (indices)
        this.winningCombinations = [
            [0, 1, 2], [3, 4, 5], [6, 7, 8], // Rows
            [0, 3, 6], [1, 4, 7], [2, 5, 8], // Columns
            [0, 4, 8], [2, 4, 6]             // Diagonals
        ];
        
        this.init();
    }
    
    init() {
        console.log('Tic-Tac-Toe initializing...');
        console.log('Found cells:', this.cells.length);
        
        // Add click event to each cell
        this.cells.forEach((cell, index) => {
            cell.addEventListener('click', () => this.handleCellClick(index));
            console.log(`Cell ${index} initialized`);
        });
        
        // Add reset button event
        this.resetBtn.addEventListener('click', () => this.resetGame());
        
        // Set initial status
        this.updateStatus();
        console.log('Tic-Tac-Toe initialized successfully!');
    }
    
    handleCellClick(index) {
        // Check if cell is already filled or game is over
        if (this.board[index] !== null || !this.gameActive) 
        {
            return;
        }
        
        // Update board state
        this.board[index] = this.currentPlayer;
        
        // Update cell UI
        this.updateCell(index);
        
        // Check for winner or draw
        if (this.checkWinner()) 
        {
            this.handleWin();
        } else if (this.checkDraw()) {
            this.handleDraw();
        } else {
            this.switchPlayer();
            this.updateStatus();
        }
    }

    updateCell(index) {
        const cell = this.cells[index];
        cell.textContent = this.currentPlayer;
        cell.classList.add(this.currentPlayer === 'X' ? 'x-mark' : 'o-mark');
        cell.disabled = true;
    }
    
    checkWinner() {
        for (const combination of this.winningCombinations) 
        {
            const [a, b, c] = combination;
            if (this.board[a] && this.board[a] === this.board[b] && this.board[a] === this.board[c]) 
            {
                this.winner = this.board[a];
                this.winningLine = combination;
                return true;
            }
        }
        return false;
    }
    
    checkDraw() {
        return this.board.every(cell => cell !== null);
    }
    
    handleWin() {
        this.gameActive = false;
        
        // Highlight winning cells
        this.winningLine.forEach(index => {
            this.cells[index].classList.add('winner');
        });
        
        // Update status
        this.statusDisplay.textContent = `Winner: ${this.winner}!`;
        this.statusDisplay.className = 'game-status status-winner';
    }
    
    handleDraw() {
        this.gameActive = false;
        this.statusDisplay.textContent = "It's a Draw!";
        this.statusDisplay.className = 'game-status status-draw';
    }

    switchPlayer() {
        this.currentPlayer = this.currentPlayer === 'X' ? 'O' : 'X';
    }
    
    updateStatus() {
        this.statusDisplay.textContent = `Next Player: ${this.currentPlayer}`;
        this.statusDisplay.className = 'game-status status-active';
    }
    
    resetGame() {
        // Reset state
        this.board = Array(9).fill(null);
        this.currentPlayer = 'X';
        this.winner = null;
        this.winningLine = [];
        this.gameActive = true;
        
        // Reset all cells
        this.cells.forEach(cell => {
            cell.textContent = '';
            cell.disabled = false;
            cell.classList.remove('x-mark', 'o-mark', 'winner');
        });
        
        // Reset status
        this.updateStatus();
    }
}

class ContactForm {
    constructor() {
        this.form = document.getElementById('contactForm');
        this.nameInput = document.getElementById('name');
        this.emailInput = document.getElementById('email');
        this.messageInput = document.getElementById('message');
        this.successMessage = document.getElementById('formSuccess');
        
        this.init();
    }
    
    init() {
        this.form.addEventListener('submit', (e) => this.handleSubmit(e));
        
        // Clear errors on input
        [this.nameInput, this.emailInput, this.messageInput].forEach(input => {
            input.addEventListener('input', () => this.clearError(input));
        });
    }
    
    handleSubmit(e) {
        e.preventDefault();
        
        // Clear previous errors
        this.clearAllErrors();
        
        // Validate form
        const isValid = this.validateForm();
        
        if (isValid) {
            this.handleSuccess();
        }
    }
    
    validateForm() {
        let isValid = true;
        
        // Validate name
        if (!this.nameInput.value.trim()) {
            this.showError(this.nameInput, 'nameError', 'Name is required');
            isValid = false;
        }
        
        // Validate email
        const emailValue = this.emailInput.value.trim();
        if (!emailValue) {
            this.showError(this.emailInput, 'emailError', 'Email is required');
            isValid = false;
        } else if (!this.isValidEmail(emailValue)) {
            this.showError(this.emailInput, 'emailError', 'Please enter a valid email');
            isValid = false;
        }
        
        // Validate message
        const messageValue = this.messageInput.value.trim();
        if (!messageValue) {
            this.showError(this.messageInput, 'messageError', 'Message is required');
            isValid = false;
        } else if (messageValue.length < 10) {
            this.showError(this.messageInput, 'messageError', 'Message must be at least 10 characters');
            isValid = false;
        }
        
        return isValid;
    }
    
    isValidEmail(email) {
        const emailRegex = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
        return emailRegex.test(email);
    }

    showError(input, errorId, message) {
        input.classList.add('error');
        const errorElement = document.getElementById(errorId);
        if (errorElement) {
            errorElement.textContent = message;
        }
    }
    
    clearError(input) {
        input.classList.remove('error');
        const errorId = input.id + 'Error';
        const errorElement = document.getElementById(errorId);
        if (errorElement) {
            errorElement.textContent = '';
        }
    }

    clearAllErrors() {
        [this.nameInput, this.emailInput, this.messageInput].forEach(input => {
            this.clearError(input);
        });
    }

    handleSuccess() {
        // Show success message
        this.successMessage.classList.remove('hidden');
        
        // Reset form
        this.form.reset();
        
        // Hide success message after 3 seconds
        setTimeout(() => {
            this.successMessage.classList.add('hidden');
        }, 3000);
        
        // Log form data (in a real app, this would send to a server)
        console.log('Form submitted:', {
            name: this.nameInput.value,
            email: this.emailInput.value,
            message: this.messageInput.value
        });
    }
}

// INITIALIZATION 

document.addEventListener('DOMContentLoaded', () => {
    // Initialize navigation
    initNavigation();
    
    // Initialize Tic-Tac-Toe game
    new TicTacToeGame();
    
    // Initialize contact form
    new ContactForm();
    
    console.log('GameHub initialized successfully!');
});
