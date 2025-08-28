export class Academies{

    static selectors = {

        DetailsofAcademies: 'group-about-transferring-academies-task-details-of-academies',
        ClickChangeacademies: 'field-academiessearch',
        searchacademy:'Data_academiesSearch-complex-field',
        academyName:'Abberley Hall',
        verifyselectedAcademy:'Data_academiesSearch-selected-items',
        ukprn:'UKPRN: 10017109',
        saveandcontinue:'save-and-continue-button',
        Markcompletecheckbox:'IsTaskCompleted',


    }

    static clickDetailsOfAcademies() 
        {

            cy.getById(this.selectors.DetailsofAcademies).contains('Details of academies').click();
        }
    static clickChangeAcademies()
        {
            cy.getById(this.selectors.ClickChangeacademies).contains('Change').click();
        }
    static searchAcademy(){
        cy.getById(this.selectors.searchacademy).type(this.selectors.academyName);
    }
    static verifyselectedAcademy() {

        cy.getById(this.selectors.verifyselectedAcademy).contains(this.selectors.academyName);
        cy.getById(this.selectors.verifyselectedAcademy).contains(this.selectors.ukprn);
    }
    
    static clickMarkCompleteCheckbox() {
        cy.getById(this.selectors.Markcompletecheckbox).check();
        cy.getById(this.selectors.Markcompletecheckbox).should('be.checked');
    }
    static clickSaveAndContinue() {
        cy.getById(this.selectors.saveandcontinue).contains('Save and continue').click();
        return this;
    }



}
export default Academies;