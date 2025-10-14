
export class ReasonsAndBenefitsIncoming{
    static selectors = {
        reasonsAndBenefitsTask: 'group-about-the-trust-that-academies-are-joining-task-reason-and-benefits',
        change: 'govuk-link',
        questions: 'govuk-summary-list__key',
        reasonsAndBenefitsSaveButton: 'reasons-and-benefits-save-button',
        strategicNeedsData: 'Data_reasonAndBenefitsAcademiesStrategicNeeds',
        maintainAndImproveData: 'Data_reasonAndBenefitsAcademiesMaintainImprove',
        benefitsTrustData: 'Data_reasonAndBenefitsAcademiesBenefitTrust',
        saveAndContinueButton: 'save-task-summary-button',
        TaskcompleteCheckbox: 'IsTaskCompleted',
        Clickchangestrategic:'field-reasonandbenefitsacademiesstrategicneeds-change-link',
        ClickchangeMaintain:'field-reasonandbenefitsacademiesmaintainimprove-change-link',
        ClickchangeBenefits:'trust',
        Taskstatus:'task-trust-details-status',
    }
        static selectReasonsAndBenefits() {
        
        cy.getById( this.selectors.reasonsAndBenefitsTask).click();

   }

        static clickReasonsIfNotStarted() {
            if (cy.getById(this.selectors.Taskstatus).contains('Not started')) {
                this.selectReasonsAndBenefits();
            }
        }
        static verifyTaskStatusIsCompleted() {
        cy.getById(this.selectors.Taskstatus).contains('Completed')
        }


}
export default ReasonsAndBenefitsIncoming;