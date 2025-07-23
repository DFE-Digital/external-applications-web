
export class ReasonsAndBenefits{
    static selectors = {
        reasonsAndBenefits: 'reasons-and-benefits',
        reasonsAndBenefitsTextArea: 'reasons-and-benefits-textarea',
        benefitsTextArea: 'benefits-textarea',
        proceedBtn: 'proceed-button'
    }

    static fillReasonsAndBenefits(reasons, benefits) {
        cy.getById(this.selectors.reasonsAndBenefitsTextArea).type(reasons);

}
}
export default ReasonsAndBenefits;
