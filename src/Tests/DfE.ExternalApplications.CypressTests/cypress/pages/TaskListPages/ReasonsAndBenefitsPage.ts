
export class ReasonsAndBenefits{
    static selectors = {
        reasonsAndBenefitsTask: 'group-about-transferring-academies-task-reason-and-benefits',
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
        Taskstatus:'task-reason-and-benefits-status',
    }
    static selectReasonsAndBenefits() {
        
        cy.getById( this.selectors.reasonsAndBenefitsTask).first().click();

}

        static clickReasonsIfNotStarted() {
            if (cy.getByClass('govuk-tag govuk-tag--grey').contains('Not started')) {
                this.selectReasonsAndBenefits();
            }
        }
    static clickchange(options: number) {
        
   cy.contains('a', 'Change').eq(options).click(); // Selects the first link
   

    }
    
        // Check if the task is not completed
        static clickChangeIfnotAnswered(options: number) {

            if (cy.getByClass('govuk-summary-list__value').contains('Not answered')) {
                this.clickchange(options);
            }
        }


   static ClickandEditChangeforStrategicNeeds(){

    // Check if the question is present before clicking change
        if (cy.getByClass(this.selectors.questions).contains('What are the strategic needs of the transferring academies and their local areas?') )
            {
                 cy.get('#field-reasonandbenefitsacademiesstrategicneeds').find(this.selectors.change).contains('Change').click();
                 cy.getById(this.selectors.strategicNeedsData).type('Strategic needs Testing text');
                 cy.getById('save-and-continue-button').click();
        }

         cy.url().should('include', '/reason-and-benefits-academies-strategic-needs'); 
        //Enter the strategic needs text
        cy.getById(this.selectors.strategicNeedsData).type('Strategic needs Testing text');
        //Click the save and continue button
        cy.SaveAndContinue();
   }



   static ClickChangeforMaintainAndImprove(){
    // Check if the question is present before clicking change
        if (cy.getByClass(this.selectors.questions).contains('How will the transferring academies help maintain and improve existing academies in the trust?') )
            {
                this.clickChangeIfnotAnswered(1);
            }

        //Enter the maintain and improve text
        cy.getById(this.selectors.maintainAndImproveData).type('Maintain and improve Testing text');
        //Click the save and continue button
        cy.SaveAndContinue();
   }
   static ClickChangeforBenefits() {

    // Check if the question is present before clicking change
        if (cy.getByClass(this.selectors.questions).contains('What are the benefits of the transfer for the transferring academies?') )
            {
                this.clickChangeIfnotAnswered(2);
            }
        cy.url().should('include', '/reason-and-benefits-academies-benefits'); 
        //Enter the benefits text
        cy.getById(this.selectors.benefitsTrustData).type('Benefits Testing text');
        //Click the save and continue button
        cy.SaveAndContinue();
    }
   
   static clickCompleteCheckbox (){

    // Check if the task is not completed
        if (cy.getById(this.selectors.TaskcompleteCheckbox).should('not.be.checked')) {
            cy.getById(this.selectors.TaskcompleteCheckbox).check();
        }
   }


static TraversingRows() {

   cy.get('#field-reasonandbenefitsacademiesstrategicneeds').find('a.govuk-link').contains('Change').click();
   cy.getById(this.selectors.strategicNeedsData).type('Strategic needs Testing text');
   cy.getById('save-and-continue-button').click();
  
    cy.get('#field-reasonandbenefitsacademiesmaintainimprove').find('a.govuk-link').contains('Change').click();
    cy.getById(this.selectors.maintainAndImproveData).type('Maintain and improve Testing text');
    cy.getById('save-and-continue-button').click();

   // cy.get('#field-reasonandbenefitsacademiesbenefittrust-change-link').contains('Change').click();
   // cy.getById(this.selectors.benefitsTrustData).type('Benefits Testing text');
   // cy.getById('save-and-continue-button').click();

}
static verifyTaskStatusIsCompleted() {
    cy.getById(this.selectors.Taskstatus).contains('Completed')
}

   // cy.getById("field...improve").contains("Change").click()
  //  cy.getByClass('govuk-summary-list').each(($row, index) => {
  // Find the link within the current row
 // cy.wrap($row).find('a').contains('Change').click();

  // Perform any assertions or actions after clicking
  //cy.url().should('include', `/expected-path-${index}`);

  // Navigate back to the table if needed
 // cy.getById('save-and-continue-button').click();});

}

export default ReasonsAndBenefits;
