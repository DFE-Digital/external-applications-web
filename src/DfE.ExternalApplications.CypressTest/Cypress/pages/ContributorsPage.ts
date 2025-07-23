
export class Contributors{

    static selectors = {
        addContributor: 'govuk-button',
        proceedBtn: 'govuk-button govuk-button--secondary',
        
    }

    
static addContributor() {
    cy.getByClass(this.selectors.addContributor).contains('Add a contributor').click();
    return this;


}

static ClickProceedBtn() {
    cy.getByClass(this.selectors.proceedBtn).contains('Proceed to the application form').click();
    return this;
}

}
export default Contributors;
