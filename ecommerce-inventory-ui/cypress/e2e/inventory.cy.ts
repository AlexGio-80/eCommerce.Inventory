describe('Inventory Page', () => {
    beforeEach(() => {
        cy.visit('/layout/inventory');
    });

    it('should display the inventory page title', () => {
        cy.contains('h1', 'Inventory').should('be.visible');
    });

    it('should load the AG-Grid table', () => {
        // Wait for the grid container to be rendered
        cy.get('.ag-grid-container', { timeout: 10000 }).should('exist');
        // Check for the AG-Grid theme class
        cy.get('.ag-theme-material', { timeout: 10000 }).should('be.visible');
    });

    it('should display grid columns', () => {
        // Wait for grid headers to load
        cy.get('.ag-header-cell', { timeout: 10000 }).should('have.length.greaterThan', 0);
    });
});
